console.log(`Created worker.`);
importScripts("./wasm.js");

onmessage = (e) => {
  let initialised = new Promise(async (resolve) => {
    // Initialize WASM module
    await wasm_bindgen(...e.data).catch((err) => {
      setTimeout(() => {
        throw err;
      });
      throw err;
    });
    // Initialize SQLite OPFS
    let res = await wasm_bindgen.install_opfs_sahpool();
    if (res !== "ok") {
      throw res;
    }
    resolve();
  });

  onmessage = async (e) => {
    // This will queue further commands up until the module is fully initialised
    await initialised;
    console.log(e.data);
    wasm_bindgen.child_entry_point(e.data);
  };

  self.postMessage("loading");
};
