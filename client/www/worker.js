console.log(`Created worker.`);
import init, {
  install_opfs_sahpool,
  child_entry_point,
  query,
} from "./wasm.js";

onmessage = (e) => {
  let initialised = new Promise(async (resolve) => {
    // Initialize WASM module
    await init(e.data).catch((err) => {
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
      self.postMessage(await query());
    } else {
      child_entry_point(e.data);
    }
  };

  self.postMessage("loading");
};
