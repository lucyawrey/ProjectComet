console.log(`Initialized worker!`);
import init, { install_opfs_sahpool } from "./wasm.js";

addEventListener("message", async (event) => {
  let cmd = event.data;
  if (cmd === "load") {
    await init();
    //self.postMessage("loaded");
    let out = await install_opfs_sahpool();
    self.postMessage(out);
  } else if (cmd === "query") {
    self.postMessage(
      "Need to move query logic here once we can ensure `load` runs first."
    );
  }
});
