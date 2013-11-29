Gopher
======

Gopher is a diagnostic tool for mirroring TCP traffic to one or more machines.

### Use Cases

Consider mirroring traffic from a live website to a staging or a test version to see if the that site behaves like the production site (or indeed improves on it).

### Overview

The tool has two modes of operation: capture and relay mode. In capture mode gopher
picks up any incoming traffic on the configured port and forwards this traffic to one
or more (upstream) relays.

When running in relay mode, gopher acts as proxy server, where it accepts incoming
HTTP traffic from a downstream gopher and then transparently proxies this traffic
to its configured upstream server. The reason to use gopher in relay mode as opposed
to directly relaying to another web server is the possibility to use relay gopher
to modify the data stream (e.g. coupling between session state and such).

### Examples

1. Start capture gopher on primary HTTP server (machineA) and relay to other server (machineB)
```
  machineA: gopher --mode=capture --port=80 --upstream=machineB:1080
```  
2. Start capture gopher and forward to 3 servers (say), B forwards to its local webserver, C sends it back
two a webserver running on machineA, port 1080 and on machineD a webserver receives the captured
traffic without relaying through a relay gopher:
```
    machineA: gopher -c -p 80 -u machineB:3280 -u machineC:3280 -u machineD

    machineB: gopher -r -p 3280 -u localhost:80

    machineC: gopher -r -p 3280 -u machineA:1080
```

### Notes
####Capture mode:
  Incoming packets to the local webserver are intercepted and distributed to
  the specified upstream server(s). It is possible to specify multiple upstream
  servers by repeating the appropriate option for each server you would like to
  add.
  
#### Relay mode:
  The application is listening for replicated requests from a downstream instance
  running in capture mode. Since the relay then establishes a meaningful two-way
  connection with the upstream HTTP server, it does not make sense to specify more
  than one upstream server (yet). Future versions may allow for multiple upstream
  servers where then an upstream server can be yet another instance running in
  relay mode.

#### N.B.
  It is not yet possible to bind/listen only to a specific IP adress on
  multihomed servers, capture more will collect any incoming packet regardless of
  destination IP and in relay mode the application will bind with IP 0.0.0.0.
  
  Please note that application is not (yet) IPv6 capable, it is explicitly limited
  to IPv4 only. Although only modest changes are required for IPv6 support...
  
### Collaborating

We accept pull requests, as far as coding standards go please try to follow the styles used.

### License

MIT license. Provided AS-IS with no warranties.
