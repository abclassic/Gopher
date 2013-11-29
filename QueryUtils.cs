using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Gopher
{
   class QueryUtils
   {
      public static System.Collections.Specialized.NameValueCollection GetQueryCollection(Uri uri)
      {
         return GetQueryCollection(uri.PathAndQuery);
      }
      public static System.Collections.Specialized.NameValueCollection GetQueryCollection(String pathAndQuery)
      {
         var idx = String.IsNullOrEmpty(pathAndQuery) ? -1 : pathAndQuery.IndexOf('?');
         return HttpUtility.ParseQueryString((idx == -1) ? String.Empty : pathAndQuery.Substring(idx));
      }

      public static String BuildPathAndQuery(String pathAndQuery, System.Collections.Specialized.NameValueCollection queryParameters)
      {
         var idx = String.IsNullOrEmpty(pathAndQuery) ? -1 : pathAndQuery.IndexOf('?');
         return (idx == -1) ? pathAndQuery : pathAndQuery.Substring(0, idx + 1) + ((queryParameters != null) ? queryParameters.ToString() : String.Empty);
      }
   }
}
