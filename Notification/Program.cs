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
using System.Notification;

namespace System.Diagnostics.Tracing
{
    // TODO this class is not likely to be in the framework itself
    /// <summary>
    /// This class is designed to 
    /// </summary>
    // TODO we would like this to be on by default.  Currently you have to do ListenToDefaultNotifier() to turn it on.  
    class NotificationEventSource : EventSource, ITelemetryListener
    {
        /// <summary>
        /// The EventSource that all Notifications get forwarded to.  
        /// </summary>
        public static NotificationEventSource LogForDefaultNotificationHub = new NotificationEventSource(TelemetryHub.DefaultDispatcher);

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

        private NotificationEventSource(ITelemetryDispatcher dispatcher)
            : base(EventSourceSettings.EtwSelfDescribingEventFormat)
        {
            m_dispatcher = dispatcher;
        }

        [NonEvent]
        bool ITelemetryListener.ShouldNotify(string notificationName)
        {
            return true;
        }

        [NonEvent]
        void ITelemetryListener.Notify(string notificationName, object parameters)
        {
            throw new NotImplementedException();
        }

        protected override void OnEventCommand(EventCommandEventArgs command)
        {
            if (m_subscription == null && IsEnabled())
                m_subscription = m_dispatcher.Subscribe(this);
            else if (m_subscription != null && !IsEnabled())
            {
                m_subscription.Dispose();
                m_subscription = null;
            }
        }

        IDisposable m_subscription;
        ITelemetryDispatcher m_dispatcher;

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
            using (var subscription = TelemetryHub.DefaultDispatcher.Subscribe(new MyNotificationListener("Default")))
            {
                // This simulates an application that is creating Dispatcher/Notifier pairs for instantiable in-process contexts.
                // The child hubs each cross-talk with the default hub, but do not cross-talk with each other
                using (var island1 = new TelemetryHub())
                using (var island2 = new TelemetryHub())
                using (island1.Dispatcher.Subscribe(new MyNotificationListener("Island1")))
                using (island2.Dispatcher.Subscribe(new MyNotificationListener("Island2")))
                {
                    // Here we simulate what might happen in the class library where we don't use dependency injection.
                    // You can also get you iNotifier by asking IServiceProvider which might make one per tenant.  
                    ITelemetrySource defaultSource = TelemetryHub.DefaultSource;
                    ITelemetrySource islandSource1 = island1.Source;
                    ITelemetrySource islandSource2 = island2.Source;

                    // Normally this would be in code that was receiving the HttpRequestResponse
                    HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, "http://localhost/");

                    // Here we log for simple cases as show below we don't need to do the ShuldNotify, but we are
                    // showing the general case where there might be significant work setting up the payload. 
                    if (defaultSource.ShouldNotify("OutgoingHttpRequestReturns"))
                    {
                        // Here we are assuming we would like to log both to EventSource and direct subscribers to
                        // NotificationHub.   Because of this the payload class contains both serializable fields (like 
                        // ReqeustUri which we resolve to a string), as well as rich objects (like Message) that are
                        // stripped from EventSource serialization.  
                        defaultSource.Notify("OutgoingHttpRequestReturns", new { RequestUri = message.RequestUri.ToString(), Message = message });
                    }

                    islandSource1.Notify("TalkingOnIsland", "Island One");

                    islandSource2.Notify("TalkingOnIsland", "Island Two");
                }
            }
        }

        class MyNotificationListener : ITelemetryListener
        {
            string m_zone;

            public MyNotificationListener(string zone)
            {
                m_zone = zone;
            }

            public void Notify(string notificationName, object parameters)
            {
                Console.WriteLine("'{2}' got a notification {0} with parameters of type {1}", notificationName, parameters, m_zone);
            }

            public bool ShouldNotify(string notificationName)
            {
                return true;
            }
        }

    }
}
