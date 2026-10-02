const NATIVE_HOST = 'com.prism.widget';
const QUOTA_FIELDS = {five_hour:'5-hour',seven_day:'Weekly',seven_day_sonnet:'Sonnet weekly',seven_day_opus:'Opus weekly'};
let running = false;
async function getJSON(path) {
  const response=await fetch('https://claude.ai'+path,{credentials:'include',cache:'no-store',redirect:'error',signal:AbortSignal.timeout(15000)});
  if(!response.ok)throw new Error(response.status===429?'Claude is rate limiting usage checks. Try later.':'Sign in at claude.ai in this browser, then click Refresh.');
  return response.json();
}
async function sync() {
  if(running)return;running=true;
  try {
    const orgs=await getJSON('/api/organizations');
    if(!Array.isArray(orgs))throw new Error('Claude returned an unknown account list.');
    const candidates=orgs.filter(o=>o&&typeof o.uuid==='string'&&Array.isArray(o.capabilities)&&o.capabilities.includes('chat'));
    const choices=(candidates.length?candidates:orgs).filter(o=>o&&typeof o.uuid==='string');
    const saved=await chrome.storage.local.get('organization');
    let org=choices.find(o=>o.uuid===saved.organization);
    await chrome.storage.local.set({organizations:choices.map(o=>({id:o.uuid,name:o.name||'Claude account'}))});
    if(!org&&choices.length===1)org=choices[0];
    if(!org)throw new Error('Choose your Claude account in the extension popup.');
    const usage=await getJSON('/api/organizations/'+encodeURIComponent(org.uuid)+'/usage');
    const windows=[];
    for(const [key,label] of Object.entries(QUOTA_FIELDS)){
      const w=usage[key]; if(!w||typeof w.utilization!=='number'||!Number.isFinite(w.utilization)||w.utilization<0)continue;
      const reset=w.resets_at==null?null:Date.parse(w.resets_at)/1000;
      if(w.resets_at!=null&&!Number.isFinite(reset))continue;
      if(reset!=null&&reset<=Date.now()/1000)continue;
      windows.push({used:w.utilization,reset,label});
    }
    if(!windows.length)throw new Error('Claude returned no current subscription quota.');
    // Only quota values cross the browser boundary. No cookies, tokens, identity or chats.
    const reply=await chrome.runtime.sendNativeMessage(NATIVE_HOST,{provider:'claude',at:Date.now()/1000,windows});
    if(!reply?.ok)throw new Error('Run Install Claude Browser Sync.cmd in the Prism folder.');
    await chrome.storage.local.set({status:'Connected · '+new Date().toLocaleTimeString(),lastSuccess:Date.now()});
    await chrome.action.setBadgeText({text:''});
  } catch(error) {
    const message=String(error.message||'Connection failed');
    const safeMessage=message.includes('native messaging')||message.includes('host')?'Run Install Claude Browser Sync.cmd in the Prism folder.':message;
    await chrome.storage.local.set({status:safeMessage});
    await chrome.action.setBadgeText({text:'!'});await chrome.action.setBadgeBackgroundColor({color:'#a6573d'});
    try{await chrome.runtime.sendNativeMessage(NATIVE_HOST,{provider:'claude',at:Date.now()/1000,error:'Claude browser sync needs attention. Open the Prism extension.'});}catch{}
  }finally{running=false;}
}
chrome.runtime.onInstalled.addListener(()=>{chrome.alarms.create('usage',{periodInMinutes:5});sync();});
chrome.runtime.onStartup.addListener(()=>{chrome.alarms.create('usage',{periodInMinutes:5});sync();});
chrome.alarms.onAlarm.addListener(alarm=>{if(alarm.name==='usage')sync();});
chrome.runtime.onMessage.addListener((message,sender,reply)=>{if(message.action==='refresh'){sync().then(()=>reply({ok:true}));return true;}});
