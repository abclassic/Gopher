//
// FiddlerExtensions.cs
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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Gopher
{
   static class SessionExtensions
   {
      public static String GetResponseBodyAsStringIfText(this Fiddler.Session oSession)
      {
         if (!oSession.bHasResponse)
            return null;
         var contentType = oSession.oResponse.headers["Content-Type"];
         if (String.IsNullOrEmpty(contentType) || !contentType.Contains("text"))
            return null;
         return oSession.GetResponseBodyEncoding().GetString(oSession.ResponseBody);
      }
      public static String utilFindRegexInResponse(this Fiddler.Session oSession, String sSearchForRegEx, Int32 group = 1)
      {
         return utilFindRegexInResponse(oSession, new Regex(sSearchForRegEx), group);
      }
      public static String utilFindRegexInResponse(this Fiddler.Session oSession, Regex rSearchForRegEx, Int32 group = 1)
      {
         var body = GetResponseBodyAsStringIfText(oSession);
         String match = null;
         if (body != null)
            Utils.RegexMatchAction(rSearchForRegEx, body, (x) => match = x, group);
         return match;
      }

   }
}
