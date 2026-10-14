// The browser side of Comet's web transport (BrowserWebSocketTransport.cs). The browser delivers WebSocket
// events between frames; each socket queues its binary messages here and C# polls for them, so nothing in
// JavaScript calls into C#. Pointers go through ptrToIdx so this works in 32-bit and 64-bit WebAssembly.
var LibraryCometWebSocket = {
  $cometWs: {
    sockets: {},
    nextId: 1,
  },

  // Opens a socket and returns its id. State: 0 connecting, 1 open, 2 closed. Connecting fails after connectMs, and
  // the socket closes once more than maxQueued bytes of messages wait untaken (they pile up while a hidden tab
  // doesn't run the game). Each ending records a reason, the numbers of Comet.Client.CloseReason, and English
  // details for logs.
  CometWs_Open__deps: ['$UTF8ToString'],
  CometWs_Open__sig: 'ipii',
  CometWs_Open: function (urlPtr, connectMs, maxQueued) {
    var ConnectFailed = 1, ConnectTimedOut = 2, ServerClosed = 3, ServerShutDown = 4, Failed = 5, FellBehind = 6;
    var id = cometWs.nextId++;
    var entry = { socket: null, state: 0, reason: 0, error: null, closing: false, queue: [], queued: 0, timer: 0 };
    cometWs.sockets[id] = entry;

    // Records why the socket ended, unless it already has a reason or this side closed it.
    function end(reason, error) {
      if (!entry.closing && !entry.reason) {
        entry.reason = reason;
        entry.error = error;
      }
    }

    // Gives up on the socket with a reason, ignoring what it reports afterwards.
    function fail(reason, error) {
      end(reason, error);
      entry.state = 2;
      entry.queue = [];
      entry.queued = 0;
      entry.socket.onopen = entry.socket.onmessage = entry.socket.onerror = entry.socket.onclose = null;
      entry.socket.close();
    }

    try {
      entry.socket = new WebSocket(UTF8ToString(urlPtr));
    } catch (e) {
      entry.state = 2;
      end(ConnectFailed, 'Could not connect: ' + (e && e.message ? e.message : e));
      return id;
    }

    entry.socket.binaryType = 'arraybuffer';
    entry.timer = setTimeout(function () {
      if (entry.state === 0) {
        fail(ConnectTimedOut, 'Timed out connecting to the server.');
      }
    }, connectMs);
    entry.socket.onopen = function () {
      clearTimeout(entry.timer);
      entry.state = 1;
    };
    entry.socket.onmessage = function (event) {
      if (event.data instanceof ArrayBuffer) {
        entry.queue.push(new Uint8Array(event.data));
        entry.queued += event.data.byteLength;
        if (entry.queued > maxQueued) {
          fail(FellBehind, "Fell too far behind the server (the game wasn't running for too long).");
        }
      }
    };
    // Browsers hide the reason for a failed connection; onclose follows with the details there are.
    entry.socket.onerror = function () {
      if (entry.state === 0) {
        end(ConnectFailed, 'Could not connect.');
      } else {
        end(Failed, 'The connection failed.');
      }
    };
    entry.socket.onclose = function (event) {
      clearTimeout(entry.timer);
      // 1001 (going away) is the server shutting down; its words, if it gave any, go in the details.
      end(event.code === 1001 ? ServerShutDown : ServerClosed, event.reason || 'The server closed the connection (code ' + event.code + ').');
      entry.state = 2;
    };
    return id;
  },

  // The reason the socket ended (Comet.Client.CloseReason), or 0 for none.
  CometWs_Reason__sig: 'ii',
  CometWs_Reason: function (id) {
    var entry = cometWs.sockets[id];
    return entry ? entry.reason : 0;
  },

  CometWs_State__sig: 'ii',
  CometWs_State: function (id) {
    var entry = cometWs.sockets[id];
    return entry ? entry.state : 2;
  },

  // The error as a newly allocated UTF-8 string that C# takes ownership of, or null for none.
  CometWs_Error__deps: ['$stringToUTF8', '$lengthBytesUTF8', 'malloc'],
  CometWs_Error__sig: 'pi',
  CometWs_Error: function (id) {
    var entry = cometWs.sockets[id];
    if (!entry || !entry.error) {
      return 0;
    }
    var size = lengthBytesUTF8(entry.error) + 1;
    var buffer = _malloc(size);
    stringToUTF8(entry.error, buffer, size);
    return buffer;
  },

  CometWs_Send__sig: 'vipi',
  CometWs_Send: function (id, dataPtr, length) {
    var entry = cometWs.sockets[id];
    if (!entry || entry.state !== 1 || entry.closing) {
      return;
    }
    // Copy out of the WebAssembly heap: it can move when memory grows, and send() may hold on to the data.
    var start = {{{ ptrToIdx('dataPtr') }}};
    entry.socket.send(HEAPU8.slice(start, start + length));
  },

  // The next received message's length, or -1 if none is waiting.
  CometWs_NextLength__sig: 'ii',
  CometWs_NextLength: function (id) {
    var entry = cometWs.sockets[id];
    return entry && entry.queue.length > 0 ? entry.queue[0].length : -1;
  },

  // Copies the next received message into the buffer (which must hold CometWs_NextLength bytes) and drops it.
  CometWs_Receive__sig: 'iipi',
  CometWs_Receive: function (id, bufferPtr, capacity) {
    var entry = cometWs.sockets[id];
    if (!entry || entry.queue.length === 0 || entry.queue[0].length > capacity) {
      return -1;
    }
    var message = entry.queue.shift();
    entry.queued -= message.length;
    HEAPU8.set(message, {{{ ptrToIdx('bufferPtr') }}});
    return message.length;
  },

  CometWs_Close__sig: 'vi',
  CometWs_Close: function (id) {
    var entry = cometWs.sockets[id];
    if (!entry || entry.closing) {
      return;
    }
    entry.closing = true;
    if (entry.socket && entry.state !== 2) {
      entry.socket.close(1000);
    } else {
      entry.state = 2;
    }
  },

  // Closes the socket if needed and forgets it.
  CometWs_Free__sig: 'vi',
  CometWs_Free: function (id) {
    var entry = cometWs.sockets[id];
    if (!entry) {
      return;
    }
    entry.closing = true;
    clearTimeout(entry.timer);
    if (entry.socket) {
      entry.socket.onopen = entry.socket.onmessage = entry.socket.onerror = entry.socket.onclose = null;
      if (entry.state !== 2) {
        entry.socket.close(1000);
      }
    }
    delete cometWs.sockets[id];
  },
};

autoAddDeps(LibraryCometWebSocket, '$cometWs');
mergeInto(LibraryManager.library, LibraryCometWebSocket);
