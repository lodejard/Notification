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

namespace System.Notification
{
    class Program
    {
        static void Main1()
        {
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
                    ITelemetry defaultTelemetry = TelemetryHub.DefaultSource.ConnectTelemetry("OutgoingHttpRequestReturns");
                    ITelemetry islandTelemetry1 = island1.Source.ConnectTelemetry("TalkingOnIsland");
                    ITelemetry islandTelemetry2 = island2.Source.ConnectTelemetry("TalkingOnIsland");

                    // Normally this would be in code that was receiving the HttpRequestResponse
                    HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, "http://localhost/");

                    // Here we log for simple cases as show below we don't need to do the ShuldNotify, but we are
                    // showing the general case where there might be significant work setting up the payload. 
                    if (defaultTelemetry.IsEnabled())
                    {
                        // Here we are assuming we would like to log both to EventSource and direct subscribers to
                        // NotificationHub.   Because of this the payload class contains both serializable fields (like 
                        // ReqeustUri which we resolve to a string), as well as rich objects (like Message) that are
                        // stripped from EventSource serialization.  
                        defaultTelemetry.Write(new { RequestUri = message.RequestUri.ToString(), Message = message });
                    }

                    islandTelemetry1.Write(new { Message = "Island One" });

                    islandTelemetry2.Write(new { Message = "Island Two" });
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

            public ITelemetry ConnectTelemetry(string name)
            {
                return new Telemetry(this, name);
            }

            class Telemetry : ITelemetry
            {
                MyNotificationListener m_self;
                string m_name;

                public Telemetry(MyNotificationListener self, string name)
                {
                    m_self = self;
                    m_name = name;
                }
                public void Write(object parameters)
                {
                    Console.WriteLine("'{2}' got a notification {0} with parameters of type {1}", m_name, parameters, m_self.m_zone);
                }

                public bool IsEnabled()
                {
                    return true;
                }
            }
        }
    }
}
