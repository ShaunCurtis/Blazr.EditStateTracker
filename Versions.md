# Versions

## 1.0.0

Initial Release

## 1.1.0

Breaking Change:

`EditStateChanged` callback removed from BlaztEditStateTracker.

There are circumstances in which using this callback to track state will loose sync with the true state.  

In any form get the dirty state directly from the BlazrEditStateStore instance. 