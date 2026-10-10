// What ShapeLand's web page tells the game (WebGLTemplates/ShapeLand). The deploy's config.js sets
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
});
