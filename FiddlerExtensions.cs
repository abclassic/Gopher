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
