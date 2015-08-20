using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Diagnostics.Tracing;
using System.Notification;

public static class TelemetryDemo
{

    static void Main()
    {
        // This allows EventSources to listen to notifications.  
        // TODO this seems counterintuitive/unnecessary.  
        TelemetryEventSource.LogForDefaultNotificationHub.Touch();

        // To set up a sink for notifications IObserver and hook up. 
        var consoleObserver = new ConsoleObserver("Default");
        using (var subscription = TelemetryListener.Default.Subscribe(consoleObserver, consoleObserver.IsEnabled))
        {
            // This simulates an application that is creating Dispatcher/Notifier pairs for instantiable in-process contexts.
            // The child hubs each cross-talk with the default hub, but do not cross-talk with each other
            using (var island1 = new TelemetryListener("Island1"))
            using (var island2 = new TelemetryListener("Island2"))
            using (island1.Subscribe(new ConsoleObserver(island1.Name)))
            using (island2.Subscribe(new ConsoleObserver(island2.Name)))
            {
                // Here we simulate what might happen in the class library where we don't use dependency injection.
                // You can also get you iNotifier by asking IServiceProvider which might make one per tenant.  
                TelemetrySource defaultSource = TelemetrySource.Default;
                TelemetrySource islandSource1 = island1;
                TelemetrySource islandSource2 = island2;

                // Normally this would be in code that was receiving the HttpRequestResponse
                HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, "http://localhost/");

                // Here we log for simple cases as show below we don't need to do the ShuldNotify, but we are
                // showing the general case where there might be significant work setting up the payload. 
                if (defaultSource.IsEnabled("OutgoingHttpRequestReturns"))
                {
                    // Here we are assuming we would like to log both to EventSource and direct subscribers to
                    // NotificationHub.   Because of this the payload class contains both serializable fields (like 
                    // ReqeustUri which we resolve to a string), as well as rich objects (like Message) that are
                    // stripped from EventSource serialization.  
                    defaultSource.WriteTelemetry("OutgoingHttpRequestReturns", new { RequestUri = message.RequestUri.ToString(), Message = message });
                }

                islandSource1.WriteTelemetry("TalkingOnIsland", "Island One");
                islandSource2.WriteTelemetry("TalkingOnIsland", "Island Two");
            }
        }
    }

    class ConsoleObserver : IObserver<KeyValuePair<string, object>>
    {
        string m_zone;

        public ConsoleObserver(string zone)
        {
            m_zone = zone;
        }
        public bool IsEnabled(string telemetryName)
        {
            return true;
        }

        public void OnCompleted()
        {
            Console.WriteLine("{0}: Done", m_zone);
        }

        public void OnError(Exception error)
        {
            Console.WriteLine("{0}: Error", m_zone);
        }

        public void OnNext(KeyValuePair<string, object> value)
        {
            Console.WriteLine("{0}: Message {1} { got value of type {2}", m_zone, value.Key, value.Value.GetType().FullName);
        }
    }


    // TODO this class is not likely to be in the framework itself
    /// <summary>
    /// This class is designed to 
    /// </summary>
    // TODO we would like this to be on by default.  Currently you have to do ListenToDefaultNotifier() to turn it on.  
    class TelemetryEventSource : EventSource, ITelemetryListener
    {
        /// <summary>
        /// The EventSource that all Notifications get forwarded to.  
        /// </summary>
        public static TelemetryEventSource LogForDefaultNotificationHub = new TelemetryEventSource(TelemetryHub.DefaultDispatcher);

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

        private TelemetryEventSource(ITelemetryDispatcher dispatcher)
            : base(EventSourceSettings.EtwSelfDescribingEventFormat)
        {
            m_dispatcher = dispatcher;
        }


        [NonEvent]
        ITelemetry ITelemetryListener.ConnectTelemetry(string name)
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
}
