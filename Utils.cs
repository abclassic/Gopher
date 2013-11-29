//
// Utils.cs
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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Gopher
{
   static class Utils
   {
      public static IPAddress ResolveAddress(String hostName)
      {
         IPAddress hostAddress = null;

         if (String.IsNullOrEmpty(hostName))
            return null;

         if (IPAddress.TryParse(hostName, out hostAddress))
            return hostAddress;

         try {
            // Get DNS host information.
            IPHostEntry hostInfo = Dns.GetHostEntry(hostName);
            hostAddress = Array.Find(hostInfo.AddressList, (x) => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
         }
         catch (Exception) {
            hostAddress = null;
         }
         return hostAddress;
      }

      /// <summary>
      /// Parse either a hostname or a IPv4 string representation of endpoint. Direct IPv6 addresses are not supported.
      /// </summary>
      /// <param name="endpoint">An endpoint in the format &lt;hostname&gt[:port] or &lt;IPaddress&gt[:port]</hostname></param>
      /// <param name="defaultPort">If no port is specified as part of the endpoint, this port is used.</param>
      /// <returns>An IPEndPoint representing the supplied input endpoint.</returns>
      public static IPEndPoint ResolveEndPoint(String endpoint, Int32 defaultPort = 80)
      {
         if (String.IsNullOrEmpty(endpoint))
            throw new ArgumentNullException("endpoint");

         var idx = endpoint.LastIndexOf(':');
         var tpl = (idx == -1 ? String.Format("{0}:{1}", endpoint, defaultPort) : endpoint).Split(new[] { ':' });

         return new IPEndPoint(ResolveAddress(tpl[0]), Int32.Parse(tpl[1]));
      }

      public static void ScheduleTimerAction(Int32 delay, TimerCallback cb, Object state)
      {
         Timer t = null;
         t = new Timer((x) => { GC.KeepAlive(t); t.Dispose(); cb(state); }, null, delay, 0);
      }

      public static void RegexMatchAction(String re, String input, Action<String> action, Int32? group = null)
      {
         RegexMatchAction(re, input, action, group);
      }

      public static void RegexMatchAction(Regex re, String input, Action<String> action, Int32? group = null)
      {
         var m = re.Match(input);
         if (m.Success)
            if (group != null)
               action(m.Groups[group.Value].Value);
            else
               action(m.Value);
      }
   }
}
