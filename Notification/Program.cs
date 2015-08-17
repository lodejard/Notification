using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Diagnostics.Tracing;
using System.Collections.Concurrent;
using System.Threading;

namespace System.Diagnostics.Tracing
{
    /**************************************************************************************************/
    // These classes belong in the framework

    /// <summary>
    /// This is the basic API to 'hook' parts of the framework.    Basically 
    /// you call out to 'interested' parties and you can pass an object (which 
    /// using anonymous types you can pass many separate data items), to your subscriber
    /// </summary>
    interface INotifier
    {
        /// <summary>
        /// Notify is a generic way of logging complex payloads.  Each notification
        /// is given a name, which identifies it as well as a object (typically an anonymous type)
        /// that gives the information to pass to the notification, which is arbitrary.  
        /// </summary>
        void Notify(string notificationName, object parameters);

        /// <summary>
        /// Optional: if there is expensive setup for the notification, you can call ShouldNotify
        /// before doing this setup.   Consumers should not be assuming that they only get notifications
        /// for which ShouldNotify is true however, it is optional for producers to call this API.  
        /// </summary>
        bool ShouldNotify(string notificationName);
    }

    /// <summary>
    /// A notificationHub is a place were notification producers (things that
    /// call INotify.Notify and subscribers (things that call AddSubscriber, and
    /// implemented INotifiy) can be hooked up.  
    /// </summary>
    /// TODO: What IObserviable support should we add if any (it could be added later)
    class NotificationHub : INotifier
    {
        /// <summary>
        /// This is the notification hub that is used by default by the class library.   
        /// Generally you don't want to make your own but rather have everyone use this one, which
        /// insures that everyone who wished to subscribe gets the callbacks.  
        /// The main reason not to us this one is that you WANT isolation from other 
        /// events in the system (e.g. multi-tenancy).  
        /// </summary>
        public static NotificationHub Default = new NotificationHub();

        /// <summary>
        /// Make a new notifier, it is a INotifier, which means the returned result can be used to 
        /// log notifications, but it also has a AddSubscriber interface so you can listen to the
        /// notifications.   Thus its job is to forward things from the producer to all the listeners
        /// (multi-casting).    Generally you should not be making your own notifier but use the
        /// Notifier.DefaultNotifier, so that notifications are as 'public' as possible.  
        /// </summary>
        public NotificationHub()
        { }

        // INotfier implementation
        public bool ShouldNotify(string notificationName)
        {
            for (var curSubscription = m_subscriptions; curSubscription != null; curSubscription = curSubscription.Next)
            {
                if (curSubscription.Subscriber.ShouldNotify(notificationName))
                    return true;
            }
            return false;
        }

        public void Notify(string notificationName, object parameters)
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
        public IDisposable Subscribe(INotifier subscriber)
        {
            Subscription newSubscription = new Subscription() { Subscriber = subscriber, Owner = this, Next = m_subscriptions };
            while (Interlocked.CompareExchange(ref m_subscriptions, newSubscription, newSubscription.Next) != newSubscription)
                newSubscription.Next = m_subscriptions;
            return newSubscription;
        }

        #region private

        // Note that Subscriptions are READ ONLY.   This means you never update any fields (even on removal!)
        private class Subscription : IDisposable
        {
            internal INotifier Subscriber;
            internal NotificationHub Owner;       // The hub this is a subscription for.  
            internal Subscription Next;           // Linked list

            public void Dispose()
            {
                // TO keep this lock free and easy to analyze, the linked list is READ ONLY.   Thus we copy

                for (; ; )
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

        Subscription m_subscriptions; // A linked list of subsciptions   Note this is ENTIRELY (DEEP) read only.   
        #endregion
    }

    /**************************************************************************************************/
    // TODO this class is not likely to be in the framework itself
    /// <summary>
    /// This class is designed to 
    /// </summary>
    // TODO we would like this to be on by default.  Currently you have to do ListenToDefaultNotifier() to turn it on.  
    class NotificationEventSource : EventSource, INotifier
    {
        /// <summary>
        /// The EventSource that all Notifications get forwarded to.  
        /// </summary>
        public static NotificationEventSource LogForDefaultNotificationHub = new NotificationEventSource(NotificationHub.Default);

        /// <summary>
        /// This is really a dummy function so that you can insure that the static variable NotificationEventSource.Log() 
        /// is touched (and thus initialized).  
        /// </summary>
        public void Touch() { }

        /// <summary>
        /// For a given notification, set the default payload for it.  This is a space separated list 
        /// strings of the form Name=Name1.Name2), which indicate which effectively give a 
        /// transformation from the notification object to the payload to be logged. 
        /// TODO do we need this?  potential for 'fighting' over it
        /// </summary>
        public void SetDefaultPayload(string notficationName, string serializationSpecification)
        {
            throw new NotImplementedException();
        }

        #region private

        private NotificationEventSource(NotificationHub notifier)
            : base(EventSourceSettings.EtwSelfDescribingEventFormat)
        {
            m_notifier = notifier;
        }

        [NonEvent]
        bool INotifier.ShouldNotify(string notificationName)
        {
            return true;
        }

        [NonEvent]
        void INotifier.Notify(string notificationName, object parameters)
        {
            throw new NotImplementedException();
        }

        protected override void OnEventCommand(EventCommandEventArgs command)
        {
            if (m_subscription == null && IsEnabled())
                m_subscription = m_notifier.Subscribe(this);
            else if (m_subscription != null && !IsEnabled())
            {
                m_subscription.Dispose();
                m_subscription = null;
            }
        }

        IDisposable m_subscription;
        NotificationHub m_notifier;

        #endregion private
    }

    class Program
    {
        static void Main()
        {
            // This allows EventSources to listen to notifications.  
            // TODO this seems counterintuitive/unnecessary.  
            NotificationEventSource.LogForDefaultNotificationHub.Touch();

            // To set up a sink for notifications you implement an INotifier and subscribe to the hub you want
            // as long as the subscription is not disposed, the INotifier will get callbacks.  
            using (var subscription = NotificationHub.Default.Subscribe(new MyNotificationReceiver()))
            {
                // Here we simulate what might happen in the class library where we don't use dependency injection.
                // You can also get you iNotifier by asking IServiceProvider which might make one per tenant.  
                INotifier iNotifier = NotificationHub.Default;

                // Normally this would be in code that was receiving the HttpRequestResponse
                HttpRequestMessage message = null;

                // Here we log for simple cases as show below we don't need to do the ShuldNotify, but we are
                // showing the general case where there might be significant work setting up the payload. 
                if (iNotifier.ShouldNotify("OutgoingHttpRequestReturns"))
                {
                    // Here we are assuming we would like to log both to EventSource and direct subscribers to
                    // NotificationHub.   Because of this the payload class contains both serializable fields (like 
                    // ReqeustUri which we resolve to a string), as well as rich objects (like Message) that are
                    // stripped from EventSource serialization.  
                    iNotifier.Notify("OutgoingHttpRequestReturns", new { RequestUri = message.RequestUri.ToString(), Message = message });
                }
            }
        }

        class MyNotificationReceiver : INotifier
        {
            public void Notify(string notificationName, object parameters)
            {
                Console.WriteLine("Got a notification {0} with parameters of type {1}", notificationName, parameters);
            }

            public bool ShouldNotify(string notificationName)
            {
                return true;
            }
        }

    }
}
