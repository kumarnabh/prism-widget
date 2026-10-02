const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
const path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../browser-extension/popup.js'),'utf8');

async function popup(organization){
  const state={organizations:[{id:'first',name:'Personal'},{id:'second',name:'Work'}],organization};
  const elements={status:{},refresh:{},org:{children:[],replaceChildren(){this.children=[];},append(option){this.children.push(option);}}};
  let refreshes=0;
  const context=vm.createContext({document:{getElementById:id=>elements[id],createElement:()=>({})},chrome:{storage:{local:{get:async()=>state,set:async value=>Object.assign(state,value)}},runtime:{sendMessage:async()=>{refreshes++;}}}});
  vm.runInContext(source,context);
  await vm.runInContext('paint()',context);
  return {elements,state,refreshes:()=>refreshes};
}
test('first organization can be selected from an explicit placeholder',async()=>{
  const p=await popup();
  assert.equal(p.elements.org.children[0].value,'');
  assert.equal(p.elements.org.children[0].selected,true);
  await p.elements.org.onchange({target:{value:'first'}});
  assert.equal(p.state.organization,'first');
  assert.equal(p.elements.org.children.length,2);
  assert.equal(p.elements.org.children[0].selected,true);
  assert.equal(p.refreshes(),1);
});
test('removed organization requires a fresh choice',async()=>{
  const p=await popup('removed');
  assert.equal(p.elements.org.children[0].value,'');
  assert.equal(p.elements.org.children[0].selected,true);
});
test('saved selection is preserved on refresh',async()=>{
  const p=await popup('second');
  await p.elements.refresh.onclick();
  assert.equal(p.state.organization,'second');
  assert.equal(p.elements.org.children[1].selected,true);
});
