ReShade's add-on API headers, unchanged.

Source:   https://github.com/crosire/reshade, tag v6.8.0, folder include/
API:      RESHADE_API_VERSION 20
Licence:  BSD-3-Clause OR MIT, (c) Patrick Mours (LICENSE.md)
Used by:  hooks/interface/reshade_bridge.cpp only.

The headers only declare ReShade's interface; ReShade itself is not part of the plugin and
is looked up in the process at run time. To update, replace the eight .hpp files and
LICENSE.md from a newer tag; a ReShade older than the headers refuses the registration.
