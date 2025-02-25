console.log(`Created worker.`);
import init, { install_opfs_sahpool } from "./wasm.js";

onmessage = (e) => {
  if (e.data === "load") {
    let initialised = init().catch((err) => {
      setTimeout(() => {
        throw err;
      });
      throw err;
    });

    onmessage = async (e) => {
      // This will queue further commands up until the module is fully initialised:
      await initialised;
      if (e.data === "query") {
        let out = await install_opfs_sahpool();
        self.postMessage(out);
      } else {
        self.postMessage("Invalid message.");
      }
    };

    self.postMessage("loading");
  }
};
