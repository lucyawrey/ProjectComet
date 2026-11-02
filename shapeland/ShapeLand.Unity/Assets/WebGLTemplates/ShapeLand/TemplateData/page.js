// ShapeLand's page: whether the servers are up, loading the game, and the phone's tap to play. Loaded as a file,
// not inline, for the page's Content Security Policy.
(function () {
  // What the build filled in (index.html's page-data): the page's text (page.* in shapeland/content/strings/en.toml,
  // put in by Builds.Web) and Unity's loader settings. Elements name their text with data-text, their aria-label with
  // data-label and their tooltip with data-title.
  var DATA = JSON.parse(document.getElementById("page-data").textContent);
  var TEXT = DATA.text;
  document.querySelectorAll("[data-text]").forEach(function (element) {
    element.textContent = TEXT[element.dataset.text];
  });
  document.querySelectorAll("[data-label]").forEach(function (element) {
    element.setAttribute("aria-label", TEXT[element.dataset.label]);
  });
  document.querySelectorAll("[data-title]").forEach(function (element) {
    element.title = TEXT[element.dataset.title];
  });

  // The Login server (window.shapelandLogin, from config.js), which says which game server to play on.
  var login = (window.shapelandLogin || "").replace(/\/$/, "");
  var body = document.body;
  var root = document.documentElement;
  var phone = root.classList.contains("phone");
  var instance = null;

  function show(state) { body.dataset.state = state; }

  // Whether the servers are up: the Login server's /api/health answers "ok". Anything else (no answer, a timeout,
  // or another host answering for its name, like a wildcard DNS record) means they aren't.
  function serverUp() {
    if (!login) {
      return Promise.resolve(true); // nothing to check; the game says if it can't reach anything
    }
    var http = login + "/api/health";
    var abort = new AbortController();
    var timer = setTimeout(function () { abort.abort(); }, 5000);
    return fetch(http, { cache: "no-store", signal: abort.signal })
      .then(function (response) { return response.ok ? response.text() : ""; })
      .then(function (text) { return text === "ok"; }, function () { return false; })
      .finally(function () { clearTimeout(timer); });
  }

  function start() {
    show("checking");
    serverUp().then(function (up) {
      if (!up) {
        show("closed");
        return;
      }
      show("loading");
      load();
    });
  }

  function load() {
    var bar = document.getElementById("progress");
    var text = document.getElementById("progress-text");
    var settings = Object.assign({}, DATA.unity, {
      arguments: [],
      // Phones draw at most 1.5 pixels per CSS pixel: sharp enough, and far cheaper than 3.
      devicePixelRatio: phone ? Math.min(window.devicePixelRatio || 1, 1.5) : undefined,
      // An error the browser hides the details of ("Script error.": from another origin, or the browser's own
      // scripts such as content blockers, as iPhone Safari reports after Tap to play) isn't the game's: logged,
      // not shown in Unity's alert. Every other error still is.
      errorHandler: function (message) {
        if (String(message).trim().indexOf("Script error") === 0) {
          console.warn("Ignored a script error the browser hid the details of.");
          return true;
        }
        return false;
      },
    });
    createUnityInstance(document.querySelector("#unity-canvas"), settings, function (progress) {
      var percent = Math.round(progress * 100);
      bar.style.width = percent + "%";
      bar.parentElement.setAttribute("aria-valuenow", String(percent));
      text.textContent = percent + "%";
    }).then(function (unityInstance) {
      instance = unityInstance;
      document.getElementById("fullscreen").disabled = false;
      if (phone && !root.classList.contains("standalone")) {
        show("tap");
      } else {
        play();
      }
    }).catch(function (message) {
      document.getElementById("failed-text").textContent = String(message);
      show("failed");
    });
  }

  document.getElementById("check").addEventListener("click", function () {
    var checked = document.getElementById("checked");
    checked.textContent = TEXT["page.checking_again"];
    serverUp().then(function (up) {
      if (up) {
        show("loading");
        load();
      } else {
        var time = new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
        checked.textContent = TEXT["page.checked_at"].replace("{time}", time);
      }
    });
  });

  document.getElementById("fullscreen").addEventListener("click", function () {
    if (instance) {
      instance.SetFullscreen(1);
    }
  });

  function play() {
    show("open");
    document.querySelector("#unity-canvas").focus();
  }

  // On a phone, the first tap makes the page full screen and turns it landscape where the browser allows it
  // (not iPhone Safari, whose card says to Add to Home Screen instead), then plays.
  document.getElementById("tap").addEventListener("click", function () {
    if (root.requestFullscreen && !document.fullscreenElement) {
      root.requestFullscreen({ navigationUI: "hide" }).then(function () {
        return screen.orientation && screen.orientation.lock ? screen.orientation.lock("landscape") : null;
      }).catch(function () {
        // Refused or unsupported: play in the browser as it is.
      });
    }
    play();
  });

  // Leaving full screen on a phone (the back gesture) brings the card back, to tap into it again.
  document.addEventListener("fullscreenchange", function () {
    if (phone && !document.fullscreenElement && body.dataset.state === "open") {
      show("tap");
    }
  });

  start();
})();
