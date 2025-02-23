import init, { install_opfs_sahpool } from "./wasm.js";

// Need to run init without running Bevy's WASM bundle.
await init();

let success = await install_opfs_sahpool();
console.log(`INSTALLED OPFS: ${success}`);
