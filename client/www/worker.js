console.log(`Created worker.`);
import init, { install_opfs_sahpool, query } from "./wasm.js";

onmessage = (e) => {
  if (e.data === "load") {
    let initialised = new Promise(async (resolve) => {
      // Initialize WASM module
      await init().catch((err) => {
        setTimeout(() => {
          throw err;
        });
        throw err;
      });
      // Initialize SQLite OPFS
      let res = await install_opfs_sahpool();
      if (res !== "ok") {
        throw res;
      }
      resolve();
    });

    onmessage = async (e) => {
      // This will queue further commands up until the module is fully initialised
      await initialised;
      if (e.data === "query") {
        let out = await query();
        self.postMessage(out);
      } else {
        self.postMessage("Invalid message.");
      }
    };

    self.postMessage("loading");
  }
};
