﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.Notification
{
    /// <summary>
    /// A notificationHub is a place were notification producers (things that
    /// call INotify.Notify and subscribers (things that call AddSubscriber, and
    /// implemented INotifiy) can be hooked up.  
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

        public static ITelemetryNotifier DefaultNotifier => s_default.Notifier;

        public ITelemetryDispatcher Dispatcher => m_dispatcher;

        public ITelemetryNotifier Notifier => m_notifier;

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
            m_notifier = new HubNotifier(m_dispatcher);

            if (connectToDefault)
            {
                var dispatcherFromDefaultNotifier = (ITelemetryDispatcher)s_default.Notifier;
                var notifierFromDefaultDispatcher = (ITelemetryNotifier)s_default.Dispatcher;
                var dispatcherFromThisNotifier = (ITelemetryDispatcher)Notifier;
                var notifierFromThisDispatcher = (ITelemetryNotifier)Dispatcher;

                m_subscriptionFromDefaultNotifier = dispatcherFromDefaultNotifier.Subscribe(notifierFromThisDispatcher);
                m_subscriptionToDefaultDispatcher = dispatcherFromThisNotifier.Subscribe(notifierFromDefaultDispatcher);
            }
        }

        public void Dispose()
        {
            m_subscriptionFromDefaultNotifier?.Dispose();
            m_subscriptionToDefaultDispatcher?.Dispose();
        }


        #region private

        private class HubDispatcher : ITelemetryDispatcher, ITelemetryNotifier
        {
            Subscription m_subscriptions; // A linked list of subsciptions   Note this is ENTIRELY (DEEP) read only.   

            // INotfier implementation
            public virtual bool ShouldNotify(string notificationName)
            {
                for (var curSubscription = m_subscriptions; curSubscription != null; curSubscription = curSubscription.Next)
                {
                    if (curSubscription.Subscriber.ShouldNotify(notificationName))
                        return true;
                }
                return false;
            }

            public virtual void Notify(string notificationName, object parameters)
            {
                for (var curSubscription = m_subscriptions; curSubscription != null; curSubscription = curSubscription.Next)
                    curSubscription.Subscriber.Notify(notificationName, parameters);
            }

            // Subscription implementation 
            /// <summary>
            /// Add a subscriber, Dispose the returned value to unsubscribe.  Every
            /// subscribers will have their ShouldNotify and Notify methods called
            /// whenever a producer is needs to cause a notification.  
            /// </summary>
            IDisposable ITelemetryDispatcher.Subscribe(ITelemetryNotifier subscriber)
            {
                Subscription newSubscription = new Subscription() { Subscriber = subscriber, Owner = this, Next = m_subscriptions };
                while (Interlocked.CompareExchange(ref m_subscriptions, newSubscription, newSubscription.Next) != newSubscription.Next)
                    newSubscription.Next = m_subscriptions;
                return newSubscription;
            }

            // Note that Subscriptions are READ ONLY.   This means you never update any fields (even on removal!)
            private class Subscription : IDisposable
            {
                internal ITelemetryNotifier Subscriber;
                internal HubDispatcher Owner;       // The hub this is a subscription for.  
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

        private class HubNotifier : HubDispatcher
        {
            public HubNotifier(HubDispatcher dispatcher)
            {
                m_dispatcher = dispatcher;
            }

            #region private
            public override bool ShouldNotify(string notificationName)
            {
                if (m_dispatcher.ShouldNotify(notificationName))
                {
                    return true;
                }
                return base.ShouldNotify(notificationName);
            }

            public override void Notify(string notificationName, object parameters)
            {
                m_dispatcher.Notify(notificationName, parameters);
                base.Notify(notificationName, parameters);
            }

            HubDispatcher m_dispatcher;
            #endregion
        }


        static TelemetryHub s_default = new TelemetryHub(connectToDefault: false);

        HubNotifier m_notifier;
        HubDispatcher m_dispatcher;
        IDisposable m_subscriptionFromDefaultNotifier;
        IDisposable m_subscriptionToDefaultDispatcher;

        #endregion
    }

}
