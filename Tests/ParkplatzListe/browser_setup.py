from pathlib import Path
import json, tempfile
repo=Path(__file__).resolve().parents[2]
out=Path(tempfile.gettempdir())/'plt-liste-browser';out.mkdir(exist_ok=True)
(out/'api.js').write_text('''import {useSyncExternalStore} from "react";
const data=new Map(), listeners=new Set();
export const bindValue=(group,key,value)=>{if(!data.has(key))data.set(key,value);return key;};
export const set=(key,value)=>{data.set(key,value);listeners.forEach(f=>f());};
export const useValue=key=>useSyncExternalStore(f=>{listeners.add(f);return()=>listeners.delete(f);},()=>data.get(key));
export const trigger=(group,key,payload)=>{
 window.calls.push({key,payload});
 if(key==='ParkplatzGebuehr' && !window.rejectFees){
  const [id,fee]=payload.split('\\t');
  setTimeout(()=>set('ParkplatzListe',data.get('ParkplatzListe').split('\\n').map(row=>{
   const f=row.split('\\t');if(f[0]===id)f[4]=fee;return f.join('\\t');}).join('\\n')),window.ackDelay||0);
 }
 if(key==='ParkplatzRundeVor'){
  const old=data.get('ParkplatzRunde');window.oldRound=old;
  set('ParkplatzRunde',`${Number(old.split('\\t')[0])+1}\\t30\\t90\\t10\\t50`);set('ParkplatzHatVorrunde',true);
 }
 if(key==='ParkplatzRundeZurueck' && window.oldRound){set('ParkplatzRunde',window.oldRound);set('ParkplatzHatVorrunde',false);}
};
window.calls=[];window.testSet=set;
const rows=[],infos=[];
for(let i=0;i<38;i++){
 const id=`${i+1}:1`, name=i===0?'Am Stadtpark – Hauptzugang':i===1?'BesucherparkplatzmitextremlangemzusammenhängendemNamenfürdieUmbruchprüfung':`Parkplatz ${String(i).padStart(2,'0')}`;
 const cap=100+i*3, used=i%4===0?cap-2:i%4===1?0:45;
 rows.push([id,name,cap,used,5,120, i===2?0:20,120,85,30,-120,i%3===0?1:0,...(i%5===0?[72,cap,0,0,0,'','']:[0,0,0,0,0,'',''])].join('\\t'));
 infos.push([id,10,2,0,0,0,'9:1','Nordmarkt',30,100,0,0,0,'9:1','Nordmarkt',50,8,18,0,0,'','',90,1,38,0,0,'',''].join('\\t'));
}
set('Sprache','de');set('ParkplatzListe',rows.join('\\n'));set('ParkplatzInfos',infos.join('\\n'));set('ParkplatzRunde','1\\t10\\t30\\t50\\t90');set('ParkplatzHatVorrunde',false);
''',encoding='utf-8')
(out/'ui.js').write_text('export const Tooltip=({children})=>children;',encoding='utf-8')
entry=f'''import React from "react";
import {{createRoot}} from "react-dom/client";
import {{ListeTab}} from {json.dumps(str(repo/'UI/src/mods/liste').replace(chr(92),'/'))};
import styles from {json.dumps(str(repo/'UI/src/mods/panel.module.scss').replace(chr(92),'/'))};
createRoot(document.getElementById('root')).render(<div className={{styles.panel}} style={{{{left:'6%',top:'3%'}}}}>
<div className={{styles.reihe}}><div className={{styles.rail}}><strong style={{{{color:'white'}}}}>PLT</strong><button className={{styles.tab}}>Draft</button><button className={{styles.tab}}>Zoning</button><button className={{`${{styles.tab}} ${{styles.tabAktiv}}`}}>Parkplätze</button></div><div className={{styles.inhalt}}><ListeTab /></div></div></div>);
'''
(out/'entry.tsx').write_text(entry,encoding='utf-8')
ui=str(repo/'UI').replace('\\','/')
config=f'''const path=require('path');
module.exports={{mode:'development',entry:path.join(__dirname,'entry.tsx'),devtool:false,
 output:{{path:__dirname,filename:'bundle.js'}},
 resolve:{{extensions:['.tsx','.ts','.js'],modules:[{json.dumps(ui+'/node_modules')}],alias:{{'cs2/api':path.join(__dirname,'api.js'),'cs2/ui':path.join(__dirname,'ui.js')}}}},
 resolveLoader:{{modules:[{json.dumps(ui+'/node_modules')}]}},
 module:{{rules:[
 {{test:/\\.tsx?$/,use:{{loader:'ts-loader',options:{{transpileOnly:true,configFile:{json.dumps(ui+'/tsconfig.json')}}}}}}},
 {{test:/\\.scss$/,use:['style-loader',{{loader:'css-loader',options:{{modules:{{localIdentName:'[local]'}}}}}},'sass-loader']}}
 ]}}
}};'''
(out/'webpack.cjs').write_text(config,encoding='utf-8')
(out/'index.html').write_text('''<!doctype html><meta charset="utf-8"><style>
html{font-size:1px;--fontSizeXS:14rem;--fontSizeS:16rem;--fontSizeL:20rem;--fontSizeXL:24rem;--fontFamily:Arial;--fontScale:1}
body{margin:0;background:#283c36;font-family:Arial;font-size:14rem;background-image:linear-gradient(30deg,#263b32,#506a4f);height:100vh}
button,input{font-family:inherit;box-sizing:border-box}button{cursor:pointer}#root{width:100%;height:100%}
</style><div id="root"></div><script src="bundle.js"></script>''',encoding='utf-8')
print(out)
