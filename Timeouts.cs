//
// Timeouts.cs
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
using System.Threading.Tasks;

namespace Gopher
{
   public static class Timeouts
   {
      // Time to wait for the RelaySite's worker thread before forceful termination.
      public static readonly Int32 EndThreadRelaySite = 250;

      // Time to wait for the PacketManager's worker threads before forceful termination.
      public static readonly Int32 EndThreadPacketManagerWorkers = 500;

      // Time to keep a closed client's IP information in case to avoid accidental processing as a new connection.
      public static readonly Int32 ClosedClientLinger = 60000;

      // If the client's last packet is received more than a certain amount of time, the client is considered orphaned and disconnected.
      public static readonly Int32 ClientOrphanThreshold = 120000;

      // Pause time between each iteration of client orphan verification
      public static readonly Int32 ClientOrphanCheckDelay = 5000;

      // Grace period to wait for out-of-order packets to arrive before sending data regardless of completeness.
      public static readonly Int32 IncompleteSequence = 750;

      // RelaySite's queue processor time to wait to check whether async connect has completed before sending packets from queue.
      public static readonly int RelaySiteConnectivityCheckDelay = 200;

      // Timeout for waiting on the acquisition of an upstream 
      public static readonly int SessionIdentifierAcquisition = 1000;
   }
}
