import { dotnet } from './_framework/dotnet.js';

const output = document.querySelector('#output');
const { setModuleImports } = await dotnet.create();
setModuleImports('main.js', {
  console: { log: (message) => { output.textContent += `\n${message}`; } },
});
await dotnet.run();
