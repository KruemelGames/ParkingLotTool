import React, { useEffect, useState } from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { sprache$ } from "./bindings";
import { Slider, MitTooltip } from "./controls";
import styles from "./vegetation.module.scss";
import base from "./panel.module.scss";
const value$ = bindValue<string>("ParkingLotTool", "Vegetation", '{"Enabled":false,"Line":false,"Density":50,"Ages":6,"Species":[]}');
const catalog$ = bindValue<string>("ParkingLotTool", "VegetationCatalog", '{"Assets":[],"Sets":[]}');
type Options = {Enabled:boolean;Line:boolean;Density:number;Ages:number;Species:string[]};
type Asset = {Id:string;Name:string;Icon:string;Tree:boolean};
type Set = {Id:string;Name:string;Icon:string;Species:string[];Custom:boolean};
export const Vegetation = () => {
 const de=useValue(sprache$)==="de";
 const t=(a:string,b:string)=>de?a:b;
 const options:Options=JSON.parse(useValue(value$));
 const catalog:{Assets:Asset[];Sets:Set[]}=JSON.parse(useValue(catalog$));
 const [limit,setLimit]=useState(60);
 const [picker,setPicker]=useState(false),[search,setSearch]=useState(""),[name,setName]=useState("");
 const send=(patch:Partial<Options>)=>trigger("ParkingLotTool","SetVegetation",JSON.stringify({...options,...patch}));
 useEffect(()=>{trigger("ParkingLotTool","RefreshVegetation");},[]);
 const select=(ids:string[])=>{
  const all=ids.every(id=>options.Species.includes(id));
  send({Species:all?options.Species.filter(id=>!ids.includes(id)):Array.from(new globalThis.Set([...options.Species,...ids]))});
 };
 const button=(label:string,selected:boolean,click:()=>void,icon?:string)=><MitTooltip text={label}><button className={`${styles.choice} ${selected?styles.selected:""}`} aria-pressed={selected} aria-label={label} onClick={click}><img alt="" src={icon||"coui://uil/Standard/TreesCustom.svg"}/></button></MitTooltip>;
 return <div className={styles.root}>
  <div className={base.schalterReihe}><span className={base.label}>Vegetation</span><MitTooltip text={t("Dekofläche 2 bepflanzen","Plant vegetation on decoration surface 2")}><button role="switch" aria-label="Vegetation" aria-checked={options.Enabled} className={`${base.schalter} ${options.Enabled?base.schalterAn:""}`} onClick={()=>send({Enabled:!options.Enabled})}><span className={`${base.schalterGriff} ${options.Enabled?base.schalterGriffAn:""}`}/></button></MitTooltip></div>
  {options.Enabled&&<div>
   <div className={styles.row}>{button(t("Frei","Free"),!options.Line,()=>send({Line:false}),"Media/Tools/Object Tool/Brush.svg")}{button("Line",options.Line,()=>send({Line:true}),"Media/Tools/Object Tool/Line.svg")}</div>
   <Slider label={t("Dichte","Density")} tooltip={t("100 %: lockere maximale Pflanzdichte mit Mindestabständen","100%: maximum planting density with spacing between plants")} value={options.Density} min={0} max={100} step={5} digits={0} unit="%" ton="Gruen" onChange={Density=>send({Density})}/>
   <div className={styles.label}>{t("Alter · Mehrfachauswahl","Age · multiple selection")}</div>
   <div className={styles.row}>{[t("Setzling","Sapling"),t("Jung","Young"),t("Ausgewachsen","Mature"),t("Alt","Elderly"),t("Tot","Dead"),t("Baumstumpf","Stump")].map((label,i)=><React.Fragment key={i}>{button(label,!!(options.Ages&(1<<i)),()=>{const Ages=options.Ages^(1<<i);if(Ages)send({Ages});},i<4?`Media/Tools/Vegetation Options/${["TreeChild","TreeTeen","TreeAdult","TreeElderly"][i]}.svg`:`coui://uil/Standard/${i===4?"TreeDead":"TreeStump"}.svg`)}</React.Fragment>)}</div>
   <div className={styles.label}>{t("Sets · Mehrfachauswahl","Sets · multiple selection")}</div>
   <div className={styles.sets}>{catalog.Sets.filter(s=>s.Species.length>0).map(s=><div key={s.Id} className={styles.set}>{button(s.Name,s.Species.every(id=>options.Species.includes(id)),()=>select(s.Species),s.Icon)}{s.Custom&&<MitTooltip text={t("Set entfernen (Pflanzenauswahl bleibt)","Remove set (keep plant selection)")}><button className={styles.remove} aria-label={t("Set entfernen","Remove set")} onClick={()=>trigger("ParkingLotTool","DeleteVegetationSet",s.Id)}>×</button></MitTooltip>}</div>)}</div>
   {button(t("Pflanzen auswählen / eigenes Set","Select plants / custom set"),picker,()=>setPicker(!picker),"coui://uil/Standard/TreesCustom.svg")}
   {picker&&<div className={styles.picker}>
    <input aria-label={t("Pflanzen suchen","Search plants")} placeholder={t("Pflanzen suchen","Search plants")} value={search} onChange={e=>setSearch(e.target.value)} onKeyDown={e=>e.stopPropagation()}/>
    <div className={styles.assets}>{catalog.Assets.filter(a=>a.Name.toLowerCase().includes(search.toLowerCase())).slice(0,limit).map(a=><React.Fragment key={a.Id}>{button(a.Name,options.Species.includes(a.Id),()=>select([a.Id]),a.Icon)}</React.Fragment>)}</div>
    {catalog.Assets.filter(a=>a.Name.toLowerCase().includes(search.toLowerCase())).length>limit&&button(t("Weitere Pflanzen","More plants"),false,()=>setLimit(limit+60),"coui://uil/Standard/Trees.svg")}
    <div className={styles.row}><input aria-label={t("Setname","Set name")} placeholder={t("Setname","Set name")} maxLength={64} value={name} onChange={e=>setName(e.target.value)} onKeyDown={e=>e.stopPropagation()}/><button className={styles.choice} aria-label={t("Speichern","Save")} title={t("Speichern","Save")} disabled={!name.trim()||options.Species.length===0} onClick={()=>{trigger("ParkingLotTool","SaveVegetationSet",JSON.stringify({Name:name,Species:options.Species}));setName("");}}><img alt="" src="coui://uil/Standard/DiskSave.svg"/></button></div>
   </div>}
   <div className={styles.label}>{t(`${options.Species.length} Arten ausgewählt`,`${options.Species.length} species selected`)}</div>
   {options.Species.some(id=>!catalog.Assets.some(a=>a.Id===id))&&<div className={styles.label}>{t("Einige gewählte Assets fehlen und werden nicht neu gepflanzt.","Some selected assets are missing and cannot be planted.")}</div>}
   {options.Species.length===0&&<div className={styles.label}>{t("Bitte ein Set oder einzelne Pflanzen wählen.","Choose a set or individual plants.")}</div>}
  </div>}
 </div>;
};
