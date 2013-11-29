using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Gopher
{
   class CookieUtils
   {
      public static Dictionary<String, String> SplitCookies(String cookieHeader)
      {
         var dct = new Dictionary<String, String>();
         if (String.IsNullOrEmpty(cookieHeader))
            return dct;
         var jar = new CookieContainer();
         var uri = new Uri(@"http://tempuri.org/");
         jar.SetCookies(uri, cookieHeader.Replace("%2C", "%252C").Replace(';', ',').UrlDecode());
         foreach (Cookie c in jar.GetCookies(uri))
            dct[c.Name] = c.Value;
         return dct;
      }
      public static String CombineCookies(Dictionary<String, String> cookies)
      {
         var jar = new CookieContainer();
         var uri = new Uri(@"http://tempuri.org/");
         var col = new CookieCollection();

         foreach (var kvp in cookies)
            col.Add(new Cookie(kvp.Key, kvp.Value));
         jar.Add(uri, col);
         return jar.GetCookieHeader(uri);
      }
      public static String RemoveCookie(String cookieHeader, String candidate)
      {
         var dict = SplitCookies(cookieHeader);
         if (!dict.ContainsKey(candidate))
            return cookieHeader;
         dict.Remove(candidate);
         return CombineCookies(dict);
      }
   }
}
