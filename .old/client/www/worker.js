console.log(`Created worker.`);
importScripts("./wasm.js");

onmessage = (e) => {
  let initialised = new Promise(async (resolve) => {
    // Initialize WASM module
    await wasm_bindgen(...e.data);
    // Initialize SQLite OPFS
    let response = await fetch("./client_data.sqlite");
    let buff = await response.arrayBuffer();
    // TODO transfer data more efficiently - ArrayBuffer.transfer() or Shared Memory
    let view = new Uint8Array(buff);
    await wasm_bindgen.install_opfs_sahpool(view);
    resolve();
  });

  onmessage = async (e) => {
    // This will queue further commands up until the module is fully initialised
    await initialised;
    wasm_bindgen.child_entry_point(e.data);
  };

  self.postMessage("loading");
};
