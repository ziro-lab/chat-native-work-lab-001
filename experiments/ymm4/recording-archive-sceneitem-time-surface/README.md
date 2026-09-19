# Recording Archive SceneItem child-time surface

## Question

Which SceneItem timing members and native host methods determine what portion of a referenced child Timeline is consumed by a parent scene?

This is relevant to archive-size optimization: the current product conservatively retains every VideoItem use in a dependency scene.

## Method

Inside real YMM4 v4.56.1.0 the probe records SceneItem/BaseItem timing-related members and scans native IL for methods referencing SceneItem.SceneId together with timing/content/frame members.

## PASS boundary

PASS identifies candidate native child-time mapping surfaces. It does not authorize dependency-range minimization until a behavioral roundtrip proves exact parent-to-child time mapping.
