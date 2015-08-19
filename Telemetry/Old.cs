
#if OLD_VERSION_1

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
        /// WriteData is a generic way of logging complex payloads.  Each notification
        /// is given a name, which identifies it as well as a object (typically an anonymous type)
        /// that gives the information to pass to the notification, which is arbitrary.  
        /// </summary>
        public abstract void WriteData(string notificationName, object parameters);

        /// <summary>
        /// Optional: if there is expensive setup for the notification, you can call ShouldNotify
        /// before doing this setup.   Consumers should not be assuming that they only get notifications
        /// for which ShouldNotify is true however, it is optional for producers to call this API.  
        /// </summary>
        public virtual bool IsEnabled(string notificationName) { return true; }
    }

    /// <summary>
    /// A TelemetryListener something that forwards on events logged with TelemetrySource 
    /// </summary>
    /// TODO: What IObserviable support should we add if any (it could be added later)
    public sealed class TelemetryListener : TelemetrySource, IDisposable
    {
        /// <summary>
        /// The TelemetrySource that you should be writing to by default.  This is what
        /// most of the class library does.   Data written here goss to DefaultListener
        /// which dispatches to all its subscribers.  
        /// </summary>
        public static TelemetrySource DefaultSource => DefaultListener;

        /// <summary>
        /// This is the TelemetryListener that is used by default by the class library.   
        /// Generally you don't want to make your own but rather have everyone use this one, which
        /// insures that everyone who wished to subscribe gets the callbacks.  
        /// The main reason not to us this one is that you WANT isolation from other 
        /// events in the system (e.g. multi-tenancy).  
        /// </summary>
        public static TelemetryListener DefaultListener => new TelemetryListener();

        /// <summary>
        /// When you subscribe to this you get callbacks for all TelemetryHubs in the appdomain
        /// as well as those that occured in the past, and all future Hubs created in the future. 
        /// </summary>
        public static event Action<TelemetryListener> AllListeners
        {
            add
            {
                lock (DefaultListener)
                {
                    // TODO: we may want to call the callbacks outside the lock
                    Delegate.Combine(s_allListenersCallback, value);

                    // Call back for each existing listener on the new callback.  
                    for (TelemetryListener cur = s_allListeners; cur != null; cur = cur.m_next)
                        value(cur);
                }
            }
            remove
            {
                Delegate.Remove(s_allListenersCallback, value);
            }
        }

        // Subscription implementation 
        /// <summary>
        /// Add a subscriber, Dispose the returned value to unsubscribe.  Every
        /// subscribers will have their IsEnabled and WriteData methods called
        /// whenever a producer is needs to cause a notification.  
        /// </summary>
        public IDisposable Subscribe(TelemetrySource subscriber)
        {
            Subscription newSubscription = new Subscription() { Subscriber = subscriber, Owner = this, Next = m_subscriptions };
            while (Interlocked.CompareExchange(ref m_subscriptions, newSubscription, newSubscription.Next) != newSubscription.Next)
                newSubscription.Next = m_subscriptions;
            return newSubscription;
        }

        /// <summary>
        /// Make a new TelemetryListener, it is a TelemetrySource, which means the returned result can be used to 
        /// log notifications, but it also has a Subscribe method so notifications can be forwarded
        /// arbitrarily.  Thus its job is to forward things from the producer to all the listeners
        /// (multi-casting).    Generally you should not be making your own TelemetryListener but use the
        /// TelemetryListener.Default, so that notifications are as 'public' as possible.  
        /// </summary>
        public TelemetryListener()
        {

            // To avoid allocating an explicit lock object I lock the Default TelemetryListener.   However there is a 
            // chicken and egg problem because I need to call this code to initialize Default.   
            var lockObj = DefaultListener;
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
        /// Telemetry hubs are kept in a list
        /// </summary>
        public void Dispose()
        {
            // Remove myself from the list if all listeners.  
            lock (DefaultListener)
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
            }
        }
#region private

        ~TelemetryListener()
        {
            Dispose();
        }

        // INotfier implementation
        public override bool IsEnabled(string notificationName)
        {
            for (var curSubscription = m_subscriptions; curSubscription != null; curSubscription = curSubscription.Next)
            {
                if (curSubscription.Subscriber.IsEnabled(notificationName))
                    return true;
            }
            return false;
        }

        public override void WriteData(string notificationName, object parameters)
        {
            for (var curSubscription = m_subscriptions; curSubscription != null; curSubscription = curSubscription.Next)
                curSubscription.Subscriber.WriteData(notificationName, parameters);
        }


        // Note that Subscriptions are READ ONLY.   This means you never update any fields (even on removal!)
        private class Subscription : IDisposable
        {
            internal TelemetrySource Subscriber;
            internal TelemetryListener Owner;          // The hub this is a subscription for.  
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

        Subscription m_subscriptions;
        TelemetryListener m_next;

        static Action<TelemetryListener> s_allListenersCallback;    // The list of clients to call back when a new TelemetryListener is created.  
        static TelemetryListener s_allListeners;                    // As a service, we keep track of all instances if TelemetryListeners.  
#endregion
    }
}

#endif