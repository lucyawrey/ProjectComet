console.log(`Created worker.`);
importScripts("./wasm.js");
let loading = true;

onmessage = ({ data }) => {
  let initialised = new Promise(async (resolve) => {
    // Initialize WASM module
    await wasm_bindgen(...data);
    // Initialize SQLite OPFS
    let response = await fetch("./client_data.sqlite");
    let bytes = await (await response.blob()).bytes();
    // TODO transfer data more efficiently - ArrayBuffer.transfer() or Shared Memory
    console.log(bytes.byteLength);
    await wasm_bindgen.install_opfs_sahpool(bytes);
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
