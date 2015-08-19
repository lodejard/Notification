
using System;
using System.Threading;
using System.Collections.Generic;

namespace System.Diagnostics.Tracing
{
    /// <summary>
    /// This is the basic API to 'hook' parts of the framework.   It is like an EventSource
    /// (which can also write object), but is intended to log complex objects that can't be serialized.
    /// 
    /// </summary>
    public abstract class TelemetrySource
    {
        /// <summary>
        /// The NotificationSource that you should be writing to by default.  This is what
        /// most of the class library does.   Data written here goss to DefaultListener
        /// which dispatches to all its subscribers.  
        /// </summary>
        public static TelemetrySource Default => TelemetryListener.Default;

        /// <summary>
        /// WriteData is a generic way of logging complex payloads.  Each notification
        /// is given a name, which identifies it as well as a object (typically an anonymous type)
        /// that gives the information to pass to the notification, which is arbitrary.  
        /// </summary>
        public abstract void WriteTelemetry(string telemetryName, object parameters);

        /// <summary>
        /// Optional: if there is expensive setup for the notification, you can call ShouldNotify
        /// before doing this setup.   Consumers should not be assuming that they only get notifications
        /// for which ShouldNotify is true however, it is optional for producers to call this API.  
        /// </summary>
        public abstract bool IsEnabled(string telemetryName);
    }

    /// <summary>
    /// A TelemetryListener something that forwards on events logged with NotificationSource 
    /// </summary>
    /// TODO: What IObserviable support should we add if any (it could be added later)
    public class TelemetryListener : TelemetrySource, IObservable<KeyValuePair<string, object>>, IDisposable
    {
        /// <summary>
        /// When a TelemetryListener is created it is given a name.   Return this.  
        /// </summary>
        public string Name { get; private set; }
        /// <summary>
        /// This is the TelemetryListener that is used by default by the class library.   
        /// Generally you don't want to make your own but rather have everyone use this one, which
        /// insures that everyone who wished to subscribe gets the callbacks.  
        /// The main reason not to us this one is that you WANT isolation from other 
        /// events in the system (e.g. multi-tenancy).  
        /// </summary>
        public static TelemetryListener Default => new TelemetryListener("TelemetryListener.Default");

        /// <summary>
        /// When you subscribe to this you get callbacks for all NotificationListeners in the appdomain
        /// as well as those that occured in the past, and all future Listeners created in the future. 
        /// </summary>
        public static event Action<TelemetryListener> AllListeners
        {
            add
            {
                lock (Default)
                {
                    // TODO: we may want to call the callbacks outside the lock
                    s_allListenersCallback = (Action<TelemetryListener>)Delegate.Combine(s_allListenersCallback, value);

                    // Call back for each existing listener on the new callback.  
                    for (TelemetryListener cur = s_allListeners; cur != null; cur = cur.m_next)
                        value(cur);
                }
            }
            remove
            {
                s_allListenersCallback = (Action<TelemetryListener>)Delegate.Remove(s_allListenersCallback, value);
            }
        }

        // Subscription implementation 
        /// <summary>
        /// Add a subscriber (Observer).  If 'IsEnabled' == null (or not present), then the Source's IsEnabled 
        /// will always return true.  
        /// </summary>
        virtual public IDisposable Subscribe(IObserver<KeyValuePair<string, object>> observer, Predicate<string> isEnabled)
        {
            Subscription newSubscription = new Subscription() { Observer = observer, IsEnabled = isEnabled, Owner = this, Next = m_subscriptions };
            while (Interlocked.CompareExchange(ref m_subscriptions, newSubscription, newSubscription.Next) != newSubscription.Next)
                newSubscription.Next = m_subscriptions;
            return newSubscription;
        }
        public IDisposable Subscribe(IObserver<KeyValuePair<string, object>> observer)
        {
            return Subscribe(observer, null);
        }

        /// <summary>
        /// Make a new TelemetryListener, it is a NotificationSource, which means the returned result can be used to 
        /// log notifications, but it also has a Subscribe method so notifications can be forwarded
        /// arbitrarily.  Thus its job is to forward things from the producer to all the listeners
        /// (multi-casting).    Generally you should not be making your own TelemetryListener but use the
        /// TelemetryListener.Default, so that notifications are as 'public' as possible.  
        /// </summary>
        public TelemetryListener(string name)
        {
            Name = name;
            // To avoid allocating an explicit lock object I lock the Default TelemetryListener.   However there is a 
            // chicken and egg problem because I need to call this code to initialize Default.   
            var lockObj = Default;
            if (lockObj == null)
                lockObj = this;

            // Insert myself into the list of all Listeners.   
            lock (lockObj)
            {
                m_next = s_allListeners;
                s_allListeners = this;
            }
        }

        /// <summary>
        /// Clean up the NotificationListeners.   Notification listeners do NOT DIE ON THEIR OWN
        /// because they are in a global list (for discoverability).  You must dispose them explicitly. 
        /// Note that we do not do the Dispose(bool) pattern because we frankly don't want to support
        /// subclasses that have non-managed state.   
        /// </summary>
        virtual public void Dispose()
        {
            // Remove myself from the list if all listeners.  
            lock (Default)
            {
                if (s_allListeners == this)
                    s_allListeners = s_allListeners.m_next;
                else
                {
                    var cur = s_allListeners;
                    while (cur != null)
                    {
                        if (cur.m_next == this)
                        {
                            cur.m_next = this.m_next;
                            break;
                        }
                        cur = cur.m_next;
                    }
                }
                m_next = null;
            }

            // TODO audit for races...  Should this be under the lock above?  

            // Indicate completion to all subscribers.  
            Subscription subscriber = null;
            Interlocked.Exchange(ref subscriber, m_subscriptions);
            while (subscriber != null)
            {
                subscriber.Observer.OnCompleted();
                subscriber = subscriber.Next;
            }
            // The code above also nulled out all subscriptions. 
            // TODO: should we block new subscriptions after we have been disposed?  
        }

        #region private

        // NotificationSource implementation
        public override bool IsEnabled(string telemetryName)
        {
            for (var curSubscription = m_subscriptions; curSubscription != null; curSubscription = curSubscription.Next)
            {
                if (curSubscription.IsEnabled == null || curSubscription.IsEnabled(telemetryName))
                    return true;
            }
            return false;
        }

        public override void WriteTelemetry(string telemetryName, object parameters)
        {
            for (var curSubscription = m_subscriptions; curSubscription != null; curSubscription = curSubscription.Next)
                curSubscription.Observer.OnNext(new KeyValuePair<string, object>(telemetryName, parameters));
        }

        // Note that Subscriptions are READ ONLY.   This means you never update any fields (even on removal!)
        private class Subscription : IDisposable
        {
            internal IObserver<KeyValuePair<string, object>> Observer;
            internal Predicate<string> IsEnabled;
            internal TelemetryListener Owner;          // The listener this is a subscription for.  
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
                return new Subscription() { Observer = subscriptions.Observer, Owner = subscriptions.Owner, Next = Remove(subscriptions.Next, subscription) };
            }
        }

        Subscription m_subscriptions;
        TelemetryListener m_next;              // We keep a linked list of all NotificationListeners (s_allListeners)

        static Action<TelemetryListener> s_allListenersCallback;    // The list of clients to call back when a new TelemetryListener is created.  
        static TelemetryListener s_allListeners;                    // As a service, we keep track of all instances if NotificationListeners.  

        #endregion
    }
}