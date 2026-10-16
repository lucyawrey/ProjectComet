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
