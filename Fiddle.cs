//
// Fiddle.cs
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
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Fiddler;

namespace Gopher
{

   /* AddUpstreamOverride(type: cookie | queryparameter, regex, func<Fiddler.Session, RequestContext, String>: function to return the key to use as associative key for the given value) */

   class Fiddle
   {
      public static void Start(IPEndPoint endpoint)
      {
         FiddlerApplication.Prefs.SetBoolPref("fiddler.network.streaming.abortifclientaborts", true);

         List<Fiddler.Session> oAllSessions = new List<Fiddler.Session>();

         // Add inline setup+redirect to BeforeRequest
         FiddlerApplication.BeforeRequest += (x) => {
            x.bBufferResponse = true;
            x.hostname = endpoint.Address.ToString();
            x.port = endpoint.Port;
         };

         FiddlerApplication.BeforeRequest += x => {
            var ctx = ClientContext.RegisterSession(x);
            ctx.QueryParameters = QueryUtils.GetQueryCollection(x.PathAndQuery);
            ctx.Cookies = CookieUtils.SplitCookies(x.oRequest.headers["Cookie"]);
         };

         FiddlerApplication.BeforeRequest += OnBeforeRequest;
         FiddlerApplication.BeforeResponse += OnBeforeResponse;

         FiddlerApplication.Startup(Program.MonitorPort, FiddlerCoreStartupFlags.AllowRemoteClients);
      }

      public static void Stop()
      {
         if (FiddlerApplication.IsStarted())
            FiddlerApplication.Shutdown();
         return;
      }


      private static void OnBeforeRequest(Fiddler.Session session)
      {
         Console.WriteLine(String.Format("REQ: {0}", session.PathAndQuery).Truncate(79, "..."));

         // Update client context with exploded cookies and query parameters.
         var ctx = ClientContext.GetContext(session);
         Debug.Assert(ctx != null);

         // TODO: Cookies
         //foreach (var ovr in EnumOverrides(ContextOverrideType.Cookie)) {
         //}

         foreach (var ovr in ClientContext.EnumOverrides(ContextOverrideType.QueryString)) {
            // before dropping the query parameter, we'll request the string that represents the associative key for this override.
            var assocKey = ovr.AssociationKeyFunction(session, ctx);
            if (String.IsNullOrEmpty(assocKey))
               continue;
            var assocVal = ClientContext.GetOverrideValue(assocKey);
            // If a mapped value exists, use it. Otherwise drop the query parameter.
            if (assocVal != null)
               ctx.QueryParameters[ovr.Name] = assocVal;
            else
               ctx.QueryParameters.Remove(ovr.Name);

         }

         // Reassemble cookies and query parameters
         session.PathAndQuery = QueryUtils.BuildPathAndQuery(session.PathAndQuery, ctx.QueryParameters);
         session.oRequest.headers["Cookie"] = ctx.Cookies.ToString();
      }

      private static void OnBeforeResponse(Session session)
      {
         //Console.WriteLine("{0}: {1}", session.responseCode, session.PathAndQuery.Truncate(70, "..."));

         var ctx = ClientContext.GetContext(session);
         Debug.Assert(ctx != null);

         // For each override we'll try to get content.
         if (ClientContext.OverrideCount > 0) {
            var body = session.GetResponseBodyAsStringIfText();
            if (body != null) {
               foreach (var ovr in ClientContext.EnumOverrides()) {
                  var assocKey = ovr.AssociationKeyFunction(session, ctx);
                  if (!String.IsNullOrEmpty(assocKey)) {
                     var assocVal = session.utilFindRegexInResponse(ovr.Expression, ovr.RegexGroup);
                     if (assocVal != null)
                        ClientContext.StoreOverrideValue(assocKey, assocVal, 30);
                  }
               }
            }
         }
      }

      private static void OnBeforeReturningError(Fiddler.Session oSession)
      {
         Console.WriteLine("ERR: Error occurred.");
         OnBeforeResponse(oSession);
      }
   }
}
