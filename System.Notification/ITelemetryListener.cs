using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System.Notification
{
    /// <summary>
    /// This is the basic API to 'hook' parts of the framework.    Basically 
    /// you call out to 'interested' parties and you can pass an object (which 
    /// using anonymous types you can pass many separate data items), to your subscriber
    /// </summary>
    public interface ITelemetryListener
    {
        ITelemetry ConnectTelemetry(string name);
    }

}
