// What ShapeLand's web page tells the game (WebGLTemplates/ShapeLand), and reloading it. The deploy's config.js sets
// window.shapelandServer when the game server isn't the page's own host.
mergeInto(LibraryManager.library, {
  // The game server's address as a newly allocated UTF-8 string that C# takes ownership of, or null for none.
  ShapeLand_PageServer__deps: ['$stringToUTF8', '$lengthBytesUTF8', 'malloc'],
  ShapeLand_PageServer__sig: 'p',
  ShapeLand_PageServer: function () {
    var server = typeof window !== 'undefined' && window.shapelandServer;
    if (!server) {
      return 0;
    }
    var size = lengthBytesUTF8(server) + 1;
    var buffer = _malloc(size);
    stringToUTF8(server, buffer, size);
    return buffer;
  },

  // Reloads the page to pick up new content, noting (for this tab only) that the game should rejoin once it's back.
  ShapeLand_ReloadToRejoin__sig: 'v',
  ShapeLand_ReloadToRejoin: function () {
    try {
      sessionStorage.setItem('shapeland.rejoin', '1');
    } catch (e) {
      // Storage blocked: the page still reloads, and the player presses Join.
    }
    location.reload();
  },

  // 1 if the page laid itself out for a phone (the "phone" class its head sets), else 0.
  ShapeLand_IsPhone__sig: 'i',
  ShapeLand_IsPhone: function () {
    return document.documentElement.classList.contains('phone') ? 1 : 0;
  },

  // 1 once the page is playing, else 0: on a phone it waits behind the "Tap to play" card until the first tap.
  ShapeLand_Playing__sig: 'i',
  ShapeLand_Playing: function () {
    return document.body.dataset.state === 'open' ? 1 : 0;
  },

  // The screen's safe area inset on one side (0 top, 1 right, 2 bottom, 3 left), as a share of the screen's height
  // (top, bottom) or width (left, right): what notches, rounded corners and the home bar cover. Read from the page's
  // #safe-area probe, so it follows turning the phone.
  ShapeLand_SafeAreaInset__sig: 'fi',
  ShapeLand_SafeAreaInset: function (side) {
    var probe = document.getElementById('safe-area');
    if (!probe || !window.innerWidth || !window.innerHeight) {
      return 0;
    }
    var style = getComputedStyle(probe);
    var value = parseFloat([style.paddingTop, style.paddingRight, style.paddingBottom, style.paddingLeft][side]) || 0;
    return value / (side % 2 === 0 ? window.innerHeight : window.innerWidth);
  },

  // 1 if the page reloaded to rejoin (once: the note is cleared), else 0.
  ShapeLand_TakeRejoin__sig: 'i',
  ShapeLand_TakeRejoin: function () {
    try {
      var rejoin = sessionStorage.getItem('shapeland.rejoin') === '1';
      sessionStorage.removeItem('shapeland.rejoin');
      return rejoin ? 1 : 0;
    } catch (e) {
      return 0;
    }
  },
});
