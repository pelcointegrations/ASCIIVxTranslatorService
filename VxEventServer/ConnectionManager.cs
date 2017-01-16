using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CPPCli;

namespace VxEventServer
{
    /// <summary>
    /// This is a helper class for handling connections to the Camera SDK. 
    /// </summary>
    static class ConnectionManager
    {
        /// <summary>
        /// lock object to ensure if multiple camera tiles connect at same time (possible when running in proxy mode) 
        /// that only one connection is made and this connection is shared between camera tiles
        /// </summary>
        private static object _loggedInSystemLock = new object();

        private const string SdkKey = "CD6DA3A342C74156F8D7EB37A8EFDC298C0A27DE1230078EF826F6E0FD6BE889";

        /// <summary>
        /// stores the Vx system that is currently connected to
        /// </summary>
        private static VXSystem _loggedInSystem = null;

        /// <summary>
        /// Connects to the StarWatch Mock Camera SDK using the information supplied
        /// </summary>
        /// <param name="ip">'ip address' to connect to.</param>
        /// <param name="username">username to connect with. </param>
        /// <param name="password">password to connect with.</param>
        /// <returns>System object if connection was successful, otherwise null</returns>
        public static VXSystem ConnectToDVR(string ip, string username, string password)
        {
            lock (_loggedInSystemLock)
            {
                if (_loggedInSystem != null)
                    return _loggedInSystem;

                _loggedInSystem = new VXSystem(ip);
                _loggedInSystem.InitializeSdk(SdkKey);
                var result = _loggedInSystem.Login(username, password);
                if (result != Results.Value.OK)
                {
                    return null;
                }
                return _loggedInSystem;
            }
        }
    }
}
