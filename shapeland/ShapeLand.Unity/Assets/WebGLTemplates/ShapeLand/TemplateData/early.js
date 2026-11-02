// Runs before the page draws (index.html loads it in <head>).
// A phone (a touch screen whose short side is phone-sized, not a tablet) gets the game filling the screen, in
// landscape. "no-fullscreen": the browser can't make the page full screen (iPhone Safari); "standalone": it
// was opened from the home screen.
(function () {
  var root = document.documentElement;
  var phone = matchMedia("(pointer: coarse)").matches && navigator.maxTouchPoints > 0 && Math.min(screen.width, screen.height) <= 600;
  root.classList.toggle("phone", phone);
  root.classList.toggle("no-fullscreen", !root.requestFullscreen);
  root.classList.toggle("standalone", navigator.standalone === true || matchMedia("(display-mode: fullscreen), (display-mode: standalone)").matches);
})();
