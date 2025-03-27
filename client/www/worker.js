console.log(`Created worker.`);
importScripts("./wasm.js");
let loading = true;

onmessage = ({ data }) => {
  let initialised = new Promise(async (resolve) => {
    // Initialize WASM module
    await wasm_bindgen(...data);

    // Initialize SQLite OPFS
    await wasm_bindgen.install_opfs_sahpool();
    let loadedDatabase = await wasm_bindgen.check_database();
    if (!loadedDatabase) {
      let response = await fetch("./client_data.sqlite");
      // TODO download and transfer data more efficiently - ArrayBuffer.transfer() or Shared Memory
      let bytes = await (await response.blob()).bytes();
      await wasm_bindgen.import_database(bytes);
    }

    // Resolve promise
    resolve();
  });

  onmessage = async ({ data }) => {
    if (loading) {
      // This will queue further commands up until the module is fully initialised
      await initialised;
      console.log(`Worker loaded.`);
      loading = false;
    }
    // wasm-bindgen-spawn call
    let result = wasm_bindgen[data]();
    postMessage(result);
  };

  postMessage("loading");
};
