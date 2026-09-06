const path=require('node:path'),os=require('node:os');
module.exports=function load(name){
  const roots=[__dirname];
  if(process.env.CODEX_NODE_MODULES) roots.push(process.env.CODEX_NODE_MODULES);
  roots.push(path.join(os.homedir(),'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules'));
  let resolved;
  for(const root of roots){try{resolved=require.resolve(name,{paths:[root]});break;}catch(e){if(e.code!=='MODULE_NOT_FOUND')throw e;}}
  if(!resolved)throw new Error('Missing '+name+'. Run npm install in this directory, or set CODEX_NODE_MODULES.');
  return require(resolved);
};
