﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.Notification
{
    /// <summary>
    /// A TelemetryListener is a place were notification producers (things that
    /// call ITelemetryNotifier.Notify and subscribers (things that call 
    /// ITelemetryDispatcher.AddSubscriber, and implemented INotifiy) can be hooked up.  
    /// </summary>
    /// TODO: What IObserviable support should we add if any (it could be added later)
    public class TelemetryHub : IDisposable
    {
        /// <summary>
        /// This is the notification hub that is used by default by the class library.   
        /// Generally you don't want to make your own but rather have everyone use this one, which
        /// insures that everyone who wished to subscribe gets the callbacks.  
        /// The main reason not to us this one is that you WANT isolation from other 
        /// events in the system (e.g. multi-tenancy).  
        /// </summary>
        public static ITelemetryDispatcher DefaultDispatcher => s_default.Dispatcher;

        public static ITelemetrySource DefaultSource => s_default.Source;

        public static event Action<TelemetryHub> AllHubs;

        public ITelemetryDispatcher Dispatcher => m_dispatcher;

        public ITelemetrySource Source => m_source;

        /// <summary>
        /// Make a new notifier, it is a INotifier, which means the returned result can be used to 
        /// log notifications, but it also has a AddSubscriber interface so you can listen to the
        /// notifications.   Thus its job is to forward things from the producer to all the listeners
        /// (multi-casting).    Generally you should not be making your own notifier but use the
        /// Notifier.DefaultNotifier, so that notifications are as 'public' as possible.  
        /// </summary>
        public TelemetryHub() : this(connectToDefault: true)
        {
        }

        public TelemetryHub(bool connectToDefault)
        {
            m_dispatcher = new HubDispatcher();
            m_source = new HubSource(m_dispatcher);

            if (connectToDefault)
            {
                var dispatcherFromDefaultNotifier = (ITelemetryDispatcher)s_default.Source;
                var listenerFromDefaultDispatcher = (ITelemetryListener)s_default.Dispatcher;
                var dispatcherFromThisNotifier = (ITelemetryDispatcher)Source;
                var listenerFromThisDispatcher = (ITelemetryListener)Dispatcher;

                m_subscriptionFromDefaultSource = dispatcherFromDefaultNotifier.Subscribe(listenerFromThisDispatcher);
                m_subscriptionToDefaultDispatcher = dispatcherFromThisNotifier.Subscribe(listenerFromDefaultDispatcher);
            }
        }

        public void Dispose()
        {
            m_subscriptionFromDefaultSource?.Dispose();
            m_subscriptionToDefaultDispatcher?.Dispose();
        }

        #region private

        private class HubDispatcher : ITelemetryDispatcher, ITelemetryListener
        {
            Subscription m_subscriptions; // A linked list of subsciptions   Note this is ENTIRELY (DEEP) read only.   
            readonly HubDispatcher m_associated;

            public HubDispatcher(HubDispatcher associated = null)
            {
                m_associated = associated;
            }
            // Subscription implementation 
            /// <summary>
            /// Add a subscriber, Dispose the returned value to unsubscribe.  Every
            /// subscribers will have their ShouldNotify and Notify methods called
            /// whenever a producer is needs to cause a notification.  
            /// </summary>
            IDisposable ITelemetryDispatcher.Subscribe(ITelemetryListener subscriber)
            {
                Subscription newSubscription = new Subscription() { Subscriber = subscriber, Owner = this, Next = m_subscriptions };
                while (Interlocked.CompareExchange(ref m_subscriptions, newSubscription, newSubscription.Next) != newSubscription.Next)
                    newSubscription.Next = m_subscriptions;
                return newSubscription;
            }

            public virtual ITelemetry ConnectTelemetry(string name)
            {
                return new Telemetry(this, name);
            }

            protected class Telemetry : ITelemetry
            {
                HubDispatcher _self;
                string _name;
                TelemetryLink _links;
                Subscription _subscriptionInstance;

                public Telemetry(HubDispatcher self, string name)
                {
                    _self = self;
                    _name = name;
                }

                public void Write(object parameters)
                {
                    for (var link = GetLinks(); link != null; link = link.Next)
                    {
                        link.Target.Write(parameters);
                    }
                }

                public bool IsEnabled()
                {
                    for (var link = GetLinks(); link != null; link = link.Next)
                    {
                        if (link.Target.IsEnabled())
                        {
                            return true;
                        }
                    }
                    return false;
                }

                TelemetryLink GetLinks()
                {
                    var subs = _self.m_subscriptions;
                    if (_subscriptionInstance == subs)
                    {
                        return _links;
                    }
                    TelemetryLink links = null;
                    if (_self.m_associated != null)
                    {
                        links = new TelemetryLink { Target = _self.m_associated.ConnectTelemetry(_name) };
                    }
                    for (var sub = subs; sub != null; sub = sub.Next)
                    {
                        links = new TelemetryLink { Next = links, Target = sub.Subscriber.ConnectTelemetry(_name) };
                    }
                    _subscriptionInstance = subs;
                    _links = links;
                    return _links;
                }

                class TelemetryLink
                {
                    public ITelemetry Target;
                    public TelemetryLink Next;
                }
            }

            // Note that Subscriptions are READ ONLY.   This means you never update any fields (even on removal!)
            private class Subscription : IDisposable
            {
                internal ITelemetryListener Subscriber;
                internal HubDispatcher Owner;         // The hub this is a subscription for.  
                internal Subscription Next;           // Linked list

                public void Dispose()
                {
                    // TO keep this lock free and easy to analyze, the linked list is READ ONLY.   Thus we copy

                    for (;;)
                    {
                        Subscription subscriptions = Owner.m_subscriptions;
                        Subscription newSubscriptions = Remove(subscriptions, this);    // Make a new list, with myself removed.  

                        // try to update, but if someone beat us to it, then retry.  
                        if (Interlocked.CompareExchange(ref Owner.m_subscriptions, newSubscriptions, subscriptions) == newSubscriptions)
                            break;
                    }
                }

                // Create a new linked list where 'subscription has been removed from the linked list of 'subscriptions'. 
                private static Subscription Remove(Subscription subscriptions, Subscription subscription)
                {
                    if (subscriptions == null)
                        return null;
                    if (subscriptions == subscription)
                        return subscriptions.Next;
                    return new Subscription() { Subscriber = subscriptions.Subscriber, Owner = subscriptions.Owner, Next = Remove(subscriptions.Next, subscription) };
                }
            }
        }

        private class HubSource : HubDispatcher, ITelemetrySource
        {
            public HubSource(HubDispatcher dispatcher) : base(dispatcher)
            {
            }

        }


        static TelemetryHub s_default = new TelemetryHub(connectToDefault: false);

        HubSource m_source;
        HubDispatcher m_dispatcher;
        IDisposable m_subscriptionFromDefaultSource;
        IDisposable m_subscriptionToDefaultDispatcher;

        #endregion
    }

}
