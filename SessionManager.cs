//
// SessionManager.cs
//
// Copyright (c) 2013 SerialsSolutions Medialab B.V.
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Gopher
{
   [Obsolete("Based on faulty premises", true)]
   class SessionManager
   {

      static SessionManager()
      {
         SessionIdentifierName = "ASP.NET_SessionId";
      }

      //////////////////////////////////////////////////////////////////////////

      /// <summary>
      /// Retrieve the upstream SessionId given a downstream IP endpoint (when there is mapping based on downstream SessionId).
      /// If a valid downstream SessionId is passed, a mapping entry is created between downstream SessionId and upstream SessionId.
      /// </summary>
      /// <param name="clientIdentifier">The downstream SessionId.</param>
      /// <param name="clientAddress">The downstream remote IP address.</param>
      /// <param name="clientPort">The downstream remote IP port.</param>
      /// <returns></returns>
      public static String GetSessionIdentifier(String clientIdentifier, IPAddress clientAddress, Int32 clientPort)
      {
         var clientIpKey = String.Format("{0}:{1}", clientAddress, clientPort);
         String id = null;

         if (!String.IsNullOrEmpty(clientIdentifier)) {
            lock (_mapSessionSession)
               id = _mapSessionSession.ContainsKey(clientIdentifier) ? _mapSessionSession[clientIdentifier] : null;
         }

         // If we found the upstream SessionId via downstream SessionId, job done.
         if (!String.IsNullOrEmpty(id))
            return id;

         // No mapping found, check if there's a mapping based on remote IP endpoint.
         lock (_mapSessionEndpoint)
            id = _mapSessionEndpoint.ContainsKey(clientIpKey) ? _mapSessionEndpoint[clientIpKey] : null;

         // If we found a mapping based on remote IP endpoint and we have a downstream SessionId, we store that one
         // to allow future lookups to match on downstream SessionId (in cases where there are multiple remote IP endpoints).
         if (!String.IsNullOrEmpty(id) && !String.IsNullOrEmpty(clientIdentifier))
            lock (_mapSessionSession)
               _mapSessionSession[clientIdentifier] = id;

         return id;
      }

      /// <summary>
      /// Updates the 'Cookie' header with the real and proxy SessionId if found, for use in outgoing requests.
      /// </summary>
      /// <param name="cookieHeader">The raw 'Cookie' header. Will be updated to reflect SessionId changes.</param>
      /// <param name="clientAddress">The remote endpoint's IP address for SessionId mapping purposes.</param>
      /// <param name="clientPort">The remote endpoint's TCP port for SessionId mapping purposes.</param>
      /// <param name="doYieldLogic">If no SessionId is found and there is a previous, outstanding request, wait for it to yield a SessionId.
      /// The first request with this parameter set to true continues (and is reponsible for acquiring a SessionId). Subsequent requests that come in while the first request is still outstanding will block.</param>
      /// <returns>True if upstream SessionId was found and inserted in Cookie header, false otherwise.</returns>
      public static Boolean ProcessRequestCookies(ref String cookieHeader, IPAddress clientAddress, Int32 clientPort, Boolean doYieldLogic = true)
      {
         var dict = CookieUtils.SplitCookies(cookieHeader);

         // Find downstream SessionId and set it in the Cookies header.
         var sessionDownstream = dict.ContainsKey(SessionIdentifierName) ? dict[SessionIdentifierName] : null;
         if (!String.IsNullOrEmpty(sessionDownstream))
            dict[ProxyIdentifierName] = sessionDownstream;

         // Find upstream SessionId and set it in the Cookies header.
         var sessionUpstream = GetSessionIdentifier(sessionDownstream, clientAddress, clientPort);
         if (!String.IsNullOrEmpty(sessionUpstream))
            dict[SessionIdentifierName] = sessionUpstream;

         // Update the cookie header if either down- or-upstream session Id was found.
         if (!String.IsNullOrEmpty(sessionDownstream) || !String.IsNullOrEmpty(sessionUpstream))
            cookieHeader = CookieUtils.CombineCookies(dict);

         // No SessionId found, end if no yield logic required.
         if (!doYieldLogic)
            return (!String.IsNullOrEmpty(sessionUpstream));

         // If there's a previous request, we'll wait on that, in the hopes of finding an upstream SessionId afterwards.
         ManualResetEvent lockEvent = null;
         lock (_sessionLock) {
            if (_sessionLock.ContainsKey(sessionDownstream))
               lockEvent = _sessionLock[sessionDownstream];
            else
               _sessionLock[sessionDownstream] = new ManualResetEvent(false);
         }
         if (lockEvent != null) {
            Console.WriteLine("SessionManager.ProcessRequestCookies() - waiting.");
            lockEvent.WaitOne(Timeouts.SessionIdentifierAcquisition);
         }

         // Wait done: retry lookup, explicitly disabling yield logic...
         return ProcessRequestCookies(ref cookieHeader, clientAddress, clientPort, doYieldLogic: false);
      }

      /// <summary>
      /// Update the session mapping to allow for upstream to downstream mapping. Signal any waiting request to continue their requests.
      /// </summary>
      /// <param name="clientIdentifier">The downstream SessionId.</param>
      /// <param name="proxyIdentifier">The upstream SessionId.</param>
      /// <param name="clientAddress">The remote endpoint's IP address for SessionId mapping purposes.</param>
      /// <param name="clientPort">The remote endpoint's TCP port for SessionId mapping purposes.</param>
      public static void ProcessResponseCookies(String clientIdentifier, String proxyIdentifier, IPAddress clientAddress, Int32 clientPort)
      {
         // Register the upstream SessionId if one is available.
         if (!String.IsNullOrEmpty(proxyIdentifier)) {
            if (!String.IsNullOrEmpty(clientIdentifier)) {
               lock (_mapSessionSession) {
                  if (!_mapSessionSession.ContainsKey(clientIdentifier))
                     _mapSessionSession[clientIdentifier] = proxyIdentifier;
               }
            } else {
               var clientIpKey = String.Format("{0}:{1}", clientAddress, clientPort);
               lock (_mapSessionEndpoint)
                  _mapSessionEndpoint[clientIpKey] = proxyIdentifier;
            }
         }

         // Signal any waiting requests.
         if (!String.IsNullOrEmpty(clientIdentifier)) {
            ManualResetEvent lockEvent = null;
            lock (_sessionLock) {
               if (_sessionLock.ContainsKey(clientIdentifier)) {
                  lockEvent = _sessionLock[clientIdentifier];
                  if (lockEvent != null) {
                     lockEvent.Set();
                     Thread.Yield();
                     //_sessionLock.Remove(clientIdentifier);
                     _sessionLock[clientIdentifier] = null;
                     lockEvent.Close();
                  }
               }
            }
         }
      }


      //////////////////////////////////////////////////////////////////////////

      public static String SessionIdentifierName
      {
         get { return _sessionIdentifierName; }
         set { _sessionIdentifierName = value; _sessionProxyName = value == null ? null : "X-Gopher-" + value; }
      }
      public static String ProxyIdentifierName { get { return _sessionProxyName; } }


      //////////////////////////////////////////////////////////////////////////

      private static String _sessionIdentifierName;
      private static String _sessionProxyName;

      private static Dictionary<String, String> _mapSessionSession = new Dictionary<String, String>();
      private static Dictionary<String, String> _mapSessionEndpoint = new Dictionary<String, String>();
      private static Dictionary<String, ManualResetEvent> _sessionLock = new Dictionary<String, ManualResetEvent>();
   }
}
