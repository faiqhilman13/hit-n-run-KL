// Kampung Run -> web page bridge: tells the loading page the city is built and drawn.
mergeInto(LibraryManager.library, {
  KampungReady: function () {
    if (typeof window !== "undefined" && window.onKampungReady) window.onKampungReady();
  }
});
