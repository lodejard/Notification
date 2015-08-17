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
    public interface INotifier
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

}
