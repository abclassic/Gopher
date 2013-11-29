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
   public enum ContextOverrideType
   {
      Cookie,
      QueryString
   }

   internal class ContextOverride
   {
      public ContextOverrideType Type;
      public String Name;
      public Regex Expression;
      public Int32 RegexGroup;
      public Func<Fiddler.Session, ClientContext, String> AssociationKeyFunction;
   }

   //////////////////////////////////////////////////////////////////////////
   //////////////////////////////////////////////////////////////////////////

   // Application-specific data we'd like to associate with a request. disposed once response has been processed.
   public class ClientContext
   {
      static ClientContext()
      {
         _expireTimer = new Timer(ExpireOverrides);
      }

      private ClientContext()
      {
         Cookies = new Dictionary<String, String>();
         QueryParameters = null;
      }

      public String GetCookie(String name)
      {
         return (!String.IsNullOrEmpty(name) && Cookies.ContainsKey(name)) ? Cookies[name] : null;
      }


      //////////////////////////////////////////////////////////////////////////

      public static ClientContext GetContext(Fiddler.Session session)
      {
         lock (_sessionContext)
            return (session != null && _sessionContext.ContainsKey(session)) ? _sessionContext[session] : null;
      }

      internal static ClientContext RegisterSession(Fiddler.Session session)
      {
         if (session == null)
            throw new ArgumentNullException("session");
         lock (_sessionContext) {
            if (_sessionContext.ContainsKey(session))
               throw new ArgumentOutOfRangeException("session", "A client context already exists for the session.");
            var ctx = new ClientContext();
            _sessionContext[session] = ctx;
            session.OnStateChanged += (o, e) => {
               if (e.newState == SessionStates.Aborted || e.newState == SessionStates.Done)
                  lock (_sessionContext)
                     _sessionContext.Remove((Session)o);
            };
            return ctx;
         }
      }

      // A context override is a way to associate a downstream value (cookie or query parameter) with a value that is contextually identical, but is exclusive to the upstream server.
      // In other words, the server keeps track of something in a value, and we need to preserve the name of the value across calls, but not the value (since that is ... specific to the server).
      // The associative key function should return a value that is unique to that session to avoid overlap with other users' overrides. (e.g. the ASP.NET_SessionId value would be a good candidate).
      // If the assocKeyFunc() returns null or "", it is taken to mean that no mapping key is available and override will be impossible to apply (maybe better luck next request?).
      // Please note that context overrides only try to match on response bodies that are of of content-type 'text[/*]'.
      public static void RegisterOverride(ContextOverrideType type, String name, String regex, Func<Fiddler.Session, ClientContext, String> assocKeyFunc, Int32 regexGroup = 1)
      {
         if (String.IsNullOrEmpty(name))
            throw new ArgumentNullException("name");
         if (String.IsNullOrEmpty(regex))
            throw new ArgumentNullException("regex");
         if (assocKeyFunc == null)
            throw new ArgumentNullException("assocKeyFunc");

         lock (_overrides) {
            // Search for an existing override and throw if it exists.
            if (OverrideExists(type, name))
               throw new InvalidOperationException("Duplicate type/name override already exists.");

            var co = new ContextOverride();
            co.Type = type;
            co.Name = name;
            co.Expression = new Regex(regex);
            co.RegexGroup = regexGroup;
            co.AssociationKeyFunction = assocKeyFunc;
            _overrides.Add(co);
         }
      }

      public static void DeregisterOverride(ContextOverrideType type, String name)
      {
         lock (_overrides)
            if (!String.IsNullOrEmpty(name))
               _overrides.RemoveAll((x) => x.Type == type && x.Name == name);
      }

      public static void DeregisterOverride(String name)
      {
         lock (_overrides)
            if (!String.IsNullOrEmpty(name))
               _overrides.RemoveAll((x) => x.Name == name);
      }

      public static Boolean OverrideExists(ContextOverrideType type, String name)
      {
         lock (_overrides)
            return (_overrides.Where((x) => x.Type == type && x.Name == name).Count() > 0);
      }

      /// <summary>
      /// Retrieve the value associated with the specified key.
      /// </summary>
      /// <param name="key">The key to retrieve the value for.</param>
      /// <returns>The requested value, null if the key is unknown.</returns>
      public static String GetOverrideValue(String key)
      {
         lock (_overrideValue)
            return (!String.IsNullOrEmpty(key) && _overrideValue.ContainsKey(key)) ? _overrideValue[key] : null;
      }

      /// <summary>
      /// Store a key and its associated value. Optionally specifiying a timeout after which the automatically key/value will be dropped.
      /// </summary>
      /// <param name="key">The key to associate the value with.</param>
      /// <param name="value">The value to store. If the value is null, the key/value is removed.</param>
      /// <param name="expirationSeconds">The delay in seconds after which the key/value is dropped.</param>
      public static void StoreOverrideValue(String key, String value, Int32 expirationSeconds = 600)
      {
         if (String.IsNullOrEmpty(key))
            throw new ArgumentNullException("key");
         lock (_overrideValue) {
            if (value != null)
               _overrideValue[key] = value;
            else
               _overrideValue.Remove(key);
         }

         // Remove the key/value from expiration.
         lock (_overrideExpiration) {
            var ptr = _overrideExpiration.FirstOrDefault(x => x.Item1 == key);
            if (ptr != null)
               _overrideExpiration.Remove(ptr);
         }

         // If there's expiration, register the key for cleanup, otherwise key lives forever (caller's responsability to cleanup).
         if (expirationSeconds > 0) {
            var expire = DateTime.Now + new TimeSpan(0, 0, expirationSeconds);

            // find a place to insert cleanup.
            lock (_overrideExpiration) {
               var ptr = _overrideExpiration.Last;
               while (ptr != null) {
                  if (ptr.Value.Item2 < expire)
                     break;
                  ptr = ptr.Previous;
               }
               if (ptr != null)
                  _overrideExpiration.AddAfter(ptr, new Tuple<String, DateTime>(key, expire));
               else
                  _overrideExpiration.AddLast(new Tuple<String, DateTime>(key, expire));
            }

            // Schedule cleanup of expired keys. If might be reasonable to assume at this point there is atleast 1 item in the queue, so it makes sense to restart
            // the cleanup timer.
            ScheduleOverrideExpiration();
         }
      }

      internal static IEnumerable<ContextOverride> EnumOverrides()
      {
         foreach (var o in _overrides) yield return o;
      }

      internal static IEnumerable<ContextOverride> EnumOverrides(ContextOverrideType type)
      {
         foreach (var o in _overrides.Where(x => x.Type == type)) yield return o;
      }

      private static void ExpireOverrides(Object state)
      {
         var expires = new List<String>();

         lock (_overrideExpiration) {
            _expireScheduled = false;

            // Since the linked list is sorted by expiration date, we'll just go on until we detect one not expired yet (or end of list ofcourse).
            var now = DateTime.Now;
            var ptr = _overrideExpiration.First;
            while (ptr != null) {
               if (ptr.Value.Item2 > now)
                  break;
               expires.Add(ptr.Value.Item1);
               _overrideExpiration.RemoveFirst();
               ptr = _overrideExpiration.First;
            }

            ScheduleOverrideExpiration();
         }

         if (expires.Count > 0) {
            lock (_overrideValue)
               foreach (var key in expires)
                  _overrideValue.Remove(key);
         }

      }

      private static void ScheduleOverrideExpiration()
      {
         // If there's items left in the queue, reschedule timer.
         lock (_overrideExpiration) {
            if (_expireScheduled)
               return;
            _expireScheduled = (_overrideExpiration.Count > 0);
            _expireTimer.Change(_expireScheduled ? Math.Max(0, (Int32)(_overrideExpiration.First.Value.Item2 - DateTime.Now).TotalSeconds) : Timeout.Infinite, Timeout.Infinite);
         }
      }

      //////////////////////////////////////////////////////////////////////////

      public NameValueCollection QueryParameters { get; internal set; }
      public Dictionary<String, String> Cookies { get; internal set; }
      public static Int32 OverrideCount { get { return _overrides.Count; } }


      //////////////////////////////////////////////////////////////////////////

      private static Dictionary<Fiddler.Session, ClientContext> _sessionContext = new Dictionary<Fiddler.Session, ClientContext>();
      private static List<ContextOverride> _overrides = new List<ContextOverride>();
      private static Dictionary<String, String> _overrideValue = new Dictionary<String, String>();
      private static LinkedList<Tuple<String, DateTime>> _overrideExpiration = new LinkedList<Tuple<String, DateTime>>();
      private static Timer _expireTimer = null;
      private static Boolean _expireScheduled = false;
   }
}
