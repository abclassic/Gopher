//
// RelayRequests.cs
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Gopher
{
   [Obsolete("Superseded by FiddlerCore implementation")]
   static class RelayRequest
   {
      public static EventHandler<RelayRequestUrlArgs> RequestUrlConstructed = null;
      public static EventHandler<RelayRequestPreRequestEventArgs> PreRequest = null;
      public static EventHandler<RelayRequestPostRequestEventArgs> PostRequest = null;


      static RelayRequest()
      {
      }

      public static HttpStatusCode ProcessRequest(HttpListenerRequest request, HttpListenerResponse response, IPEndPoint relay)
      {
         HttpWebRequest relayRequest = null;
         HttpWebResponse relayResponse = null;
         Stream relayRequestStream = null;

         try {
            var urlRelay = String.Format("http://{0}{1}{2}", relay.Address, (relay.Port == 80) ? string.Empty : ":" + relay.Port, request.RawUrl);
            urlRelay = Fire_RequestUrlConstructed(relay, request.Url.AbsolutePath, request.Url.Query, urlRelay);
            relayRequest = (HttpWebRequest)HttpWebRequest.Create(urlRelay);
            RelayServer.RegisterRelayRequest(request.RemoteEndPoint, relayRequest);
            relayRequestStream = request.HasEntityBody ? relayRequest.GetRequestStream() : null;

            // Assign client's 'persistent' cookie jar.
            relayRequest.CookieContainer = new CookieContainer();
            // Create unique connection group to avoid connection mixing with other clients' connections.
            relayRequest.ConnectionGroupName = String.Format("{0}:{1}", request.RemoteEndPoint.Address, request.RemoteEndPoint.Port);
            var bla = relayRequest.ServicePoint.ConnectionName;

            TransposeRequestHeaders(request, relayRequest);
            TransposeRequestCookies(request, relayRequest);
            Fire_PreRequest(request, relayRequest);
            PipeStream(request.InputStream, relayRequestStream);

            try {
               relayResponse = (HttpWebResponse)relayRequest.GetResponse();
            }
            catch (WebException e) {
               relayResponse = e.Response as HttpWebResponse;
            }
            if (relayResponse == null)
               return HttpStatusCode.InternalServerError;

            TransposeResponseHeaders(relayResponse, response);
            Fire_PostRequest(request, relayRequest, relayResponse, response);
            PipeStream(relayResponse.GetResponseStream(), response.OutputStream);
         }
         finally {
            if (relayResponse != null)
               relayResponse.Close();
            if (relayRequestStream != null)
               relayRequestStream.Close();
            RelayServer.DeregisterRelayRequest(request.RemoteEndPoint);
         }

         return relayResponse.StatusCode;
      }


      //////////////////////////////////////////////////////////////////////////

      private static String Fire_RequestUrlConstructed(IPEndPoint server, String path, String query, String url)
      {
         String urlResponse = null;
         try {
            if (RequestUrlConstructed != null) {
               var args = new RelayRequestUrlArgs() { Server = server, Path = path, QueryString = query, RequestUrl = url };
               RequestUrlConstructed(null, args);
               urlResponse = args.RequestUrl;
            }
         }
         catch (Exception) { urlResponse = null; }

         return !String.IsNullOrEmpty(urlResponse) ? urlResponse : url;
      }

      private static void Fire_PreRequest(HttpListenerRequest origin, HttpWebRequest request)
      {
         try {
            if (PreRequest != null)
               PreRequest(null, new RelayRequestPreRequestEventArgs() { Origin = origin, Request = request });
         }
         catch (Exception) { }
      }

      private static void Fire_PostRequest(HttpListenerRequest origin, HttpWebRequest request, HttpWebResponse response, HttpListenerResponse destination)
      {
         try {
            if (PostRequest != null)
               PostRequest(null, new RelayRequestPostRequestEventArgs() { Origin = origin, Request = request, Response = response, Destination = destination });
         }
         catch (Exception) { }
      }


      //////////////////////////////////////////////////////////////////////////

      private static void TransposeRequestHeaders(HttpListenerRequest request, HttpWebRequest relay)
      {
         var headers = request.Headers;
         foreach (String header in headers) {
            if (!_restrictedHeadersRequest.Contains(header))
               relay.Headers.Add(header, headers[header]);
         }

         // Headers accessible only via object properties.
         relay.Accept = headers["Accept"];
         // relay.Connection
         relay.ContentType = request.ContentType;
         // relay.Date
         relay.Expect = headers["Expect"];
         relay.Host = request.UserHostName;
         // relay.IfModifiedSince
         relay.KeepAlive = request.KeepAlive;
         // relay.Proxy
         // relay.Range
         relay.Referer = headers["Referer"];
         relay.TransferEncoding = headers["Transfer-Encoding"];
         relay.UserAgent = request.UserAgent;
      }

      private static void TransposeResponseHeaders(HttpWebResponse relay, HttpListenerResponse response)
      {
         var headers = relay.Headers;
         foreach (String header in headers) {
            if (!_restrictedHeadersResponse.Contains(header))
               response.Headers.Add(header, headers[header]);
         }
      }

      private static void TransposeRequestCookies(HttpListenerRequest request, HttpWebRequest relayRequest)
      {
         Debug.Assert(relayRequest.CookieContainer != null);

         lock (relayRequest.CookieContainer) {
            foreach (Cookie cookie in request.Cookies)
               if (!_restrictedHeadersRequest.Contains(cookie.Name))
                  relayRequest.CookieContainer.Add(relayRequest.RequestUri, cookie);
         }
      }

      private static void TransposeResponseCookies(HttpWebResponse relay, HttpListenerResponse response)
      {
         foreach (Cookie cookie in relay.Cookies)
            if (!_restrictedHeadersResponse.Contains(cookie.Name))
               response.Cookies.Add(cookie);
      }


      //////////////////////////////////////////////////////////////////////////

      private static void PipeStream(Stream @in, Stream @out, Int32 bufSize = 1024)
      {
         if (@in != null && @out != null && @in.CanRead && @out.CanWrite) {
            var buffer = new Byte[bufSize];
            var length = 0;
            while ((length = @in.Read(buffer, 0, buffer.Length)) != 0)
               @out.Write(buffer, 0, length);
         }
         return;
      }


      #region Restricted Headers
      // Source: System.Net.HeaderInfoTable
      //
      // List of restricted headers in a request
      private static List<String> _restrictedHeadersRequest = new List<String>(new String[] {
         "Accept", "Connection", "Content-Type", "Content-Length", "Date",
         "Expect", "Host", "If-Modified-Since", "Keep-Alive", "Proxy-Connection",
         "Range", "Referer", "Transfer-Encoding", "User-Agent"
      });

      // List of restricted headers in a request
      private static List<String> _restrictedHeadersResponse = new List<String>(new String[] {
         "In-Response", "Content-Length", "Keep-Alive", "Transfer-Encoding", "WWW-Authenticate"
      });
      #endregion
   }
}
