async function paint(){
  const data=await chrome.storage.local.get(['status','organizations','organization']);
  document.getElementById('status').textContent=data.status||'Open Claude and sign in, then refresh.';
  const select=document.getElementById('org'), organizations=data.organizations||[];
  select.replaceChildren();
  if(organizations.length>1&&!organizations.some(org=>org.id===data.organization)){
    const placeholder=document.createElement('option');
    placeholder.value='';placeholder.textContent='Choose an account';placeholder.disabled=true;placeholder.selected=true;
    select.append(placeholder);
  }
  for(const org of organizations){const option=document.createElement('option');option.value=org.id;option.textContent=org.name;option.selected=org.id===data.organization;select.append(option);}
  select.hidden=organizations.length<2;
}
document.getElementById('refresh').onclick=async()=>{document.getElementById('status').textContent='Refreshing…';await chrome.runtime.sendMessage({action:'refresh'});await paint();};
document.getElementById('org').onchange=async e=>{await chrome.storage.local.set({organization:e.target.value});await chrome.runtime.sendMessage({action:'refresh'});await paint();};
paint();
