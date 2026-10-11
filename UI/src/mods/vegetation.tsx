import React, { useEffect, useState } from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { useTexte } from "./texte";
import { Slider, MitTooltip, Fenster, Suchfeld, Eingabefeld, SettingActions } from "./controls";
import styles from "./vegetation.module.scss";
import base from "./panel.module.scss";
import { useListenposition } from "./listenposition";
// Nutzerwunsch 2026-10-09: Tiefe Scrollpositionen brauchen auch nach dem
// Schliessen die zuvor mit "mehr Pflanzen" eingeblendeten Arten.
let gemerktesLimit = 60;
const value$ = bindValue<string>("ParkingLotTool", "Vegetation", '{"Enabled":false,"Line":false,"Density":50,"Ages":6,"NoAging":true,"Species":[]}');
const default$ = bindValue<string>("ParkingLotTool", "VegetationDefault", '{"Enabled":false,"Line":false,"Density":50,"Ages":6,"NoAging":true,"Species":[]}');
const catalog$ = bindValue<string>("ParkingLotTool", "VegetationCatalog", '{"Assets":[],"Sets":[]}');
type Options = {Enabled:boolean;Line:boolean;Density:number;Ages:number;NoAging:boolean;Seed?:number;Species:string[]};
type Asset = {Id:string;Name:string;Icon:string;Tree:boolean};
type Set = {Id:string;Name:string;Icon:string;Species:string[];Custom:boolean};

/**
 * DER VEGETATIONSBLOCK IST EIN EIGENES FENSTER GEWORDEN.
 *
 * Er stand bis zum 2026-09-17 in der Gruen-Spalte des Panels: Muster,
 * Dichte, sechs Altersknoepfe, Sets, der Katalogknopf und zwei Hinweise.
 * Sobald man Vegetation einschaltete, wuchs die Spalte um gut sechs Zeilen
 * und damit das ganze Panel. Der Nutzer: *"Das PLT-Panel oeffnet sich nach
 * unten."*
 *
 * Im Panel bleibt deshalb nur der Schalter. Alles andere liegt im Fenster,
 * und das Panel hat wieder eine feste Hoehe.
 *
 * ZWEI TEILE, ausdrueckliche Vorgabe: links die gewoehnlichen
 * Einstellungen, rechts die Pflanzenauswahl fuers eigene Set. Rechts geht
 * auf, wenn man ein Set anlegt, und ein kleiner Knopf am Rand halbiert das
 * Fenster wieder.
 */

/** Gemeinsamer Werkzeugkasten beider Teile. */
const useVegetation = () => {
 const texte=useTexte();
 const t=texte.text;
 const options:Options=JSON.parse(useValue(value$));
 const catalog:{Assets:Asset[];Sets:Set[]}=JSON.parse(useValue(catalog$));
 const send=(patch:Partial<Options>)=>trigger("ParkingLotTool","SetVegetation",JSON.stringify({...options,...patch}));
 const select=(ids:string[])=>{
  const all=ids.every(id=>options.Species.includes(id));
  send({Species:all?options.Species.filter(id=>!ids.includes(id)):Array.from(new globalThis.Set([...options.Species,...ids]))});
 };
 const button=(label:string,selected:boolean,click:()=>void,icon?:string,disabled=false)=><MitTooltip text={label}><button className={`${styles.choice} ${selected?styles.selected:""}`} aria-pressed={selected} aria-label={label} disabled={disabled} onClick={click}><img alt="" src={icon||"coui://uil/Standard/TreesCustom.svg"}/></button></MitTooltip>;
 return {t,zahl:texte.zahl,options,catalog,send,select,button};
};

/**
 * Was im Panel stehenbleibt: Name und Schalter.
 *
 * Der NAME ist anklickbar und oeffnet das Fenster. Er sieht im Ruhezustand
 * nicht nach Knopf aus - ein Panel voller Schaltflaechen, von denen die
 * Haelfte nur Text ist, liest sich schlechter als eines mit einer
 * Hoverflaeche. Ansage des Nutzers am 2026-09-17.
 */
export const VegetationSchalter = ({ onOeffnen }: {
 onOeffnen: (an: boolean) => void;
}) => {
 const {t,options,send}=useVegetation();
 useEffect(()=>{trigger("ParkingLotTool","RefreshVegetation");},[]);
 return <div className={styles.root}>
  <div className={base.schalterReihe}>
   <MitTooltip text={t("vegetation.openTheVegetationSettings")}>
    <button className={`${base.label} ${styles.nameKnopf}`} onClick={()=>onOeffnen(true)}>{t("vegetation.vegetation")}</button>
   </MitTooltip>
   <MitTooltip text={t("vegetation.plantVegetationOnDecorationSurface")}>
    <button role="switch" aria-label={t("vegetation.vegetation")} aria-checked={options.Enabled}
     className={`${base.schalter} ${options.Enabled?base.schalterAn:""}`}
     onClick={()=>{const an=!options.Enabled;send({Enabled:an});onOeffnen(an);}}>
     <span className={`${base.schalterGriff} ${options.Enabled?base.schalterGriffAn:""}`}/>
    </button>
   </MitTooltip>
  </div>
 </div>;
};

/** Breite der beiden Haelften in rem. */
const BREITE_LINKS = 300;
const BREITE_RECHTS = 340;

export const VegetationFenster = ({ pos, onPos, onClose }: {
 pos?: {x:number;y:number};
 onPos?: (p:{x:number;y:number})=>void;
 onClose: () => void;
}) => {
 const {t,zahl,options,catalog,send,select,button}=useVegetation();
 const [rechts,setRechts]=useState(false);
 const [limit,setLimit]=useState(gemerktesLimit);
 const [search,setSearch]=useState("");
 const [name,setName]=useState("");
 const treffer=catalog.Assets.filter(a=>a.Name.toLowerCase().includes(search.toLowerCase()));
 const listenposition=useListenposition("vegetation.arten",rechts,search!=="",Math.min(treffer.length,limit));
 useEffect(()=>{
  if(!rechts){setSearch("");setLimit(gemerktesLimit);}
 },[rechts]);
 const standard:Options=JSON.parse(useValue(default$));
 const ohneSeed=(o:Options)=>JSON.stringify({...o,Seed:0,Species:[...(o.Species||[])].sort()});
 const abweichend=ohneSeed(options)!==ohneSeed(standard);
 return <Fenster titel={t("vegetation.vegetation")}
   breite={rechts?BREITE_LINKS+BREITE_RECHTS:BREITE_LINKS}
   pos={pos} onPos={onPos} vonUnten onClose={onClose}>
  <div className={styles.zweiTeile}>

   <div className={styles.teilLinks}>
    {/* Speichern/Zuruecksetzen fuer das ganze Fenster - dieselben Knoepfe
        wie an jedem Regler im Panel. Die Zufallszahl zaehlt nicht mit. */}
    <div className={styles.row}>
     <div className={styles.label} style={{flex:"1 1 0"}}>{t("laternen.defaultForTheWholeWindow")}</div>
     <SettingActions label={t("vegetation.vegetation")} active={abweichend}
      onReset={()=>trigger("ParkingLotTool","ResetVegetation")}
      onSetDefault={()=>trigger("ParkingLotTool","SaveVegetationDefault")}/>
    </div>
    <div className={styles.row}>{button(t("vegetation.free"),!options.Line,()=>send({Line:false}),"Media/Tools/Object Tool/Brush.svg")}{button(t("vegetation.line"),options.Line,()=>send({Line:true}),"Media/Tools/Object Tool/Line.svg")}</div>
    <Slider label={t("vegetation.density")} tooltip={t("vegetation.100MaximumPlantingDensityWith")} value={options.Density} min={0} max={100} step={5} digits={0} unit={t("einheit.prozent")} ton="Gruen" onChange={Density=>send({Density})}/>
    <div className={styles.label}>{t("vegetation.ageMultipleSelection")}</div>
    <div className={styles.row}>{[t("vegetation.sapling"),t("vegetation.young"),t("vegetation.mature"),t("vegetation.elderly"),t("vegetation.dead"),t("vegetation.stump")].map((label,i)=><React.Fragment key={i}>{button(label,!!(options.Ages&(1<<i)),()=>{const Ages=options.Ages^(1<<i);if(Ages)send({Ages});},i<4?`Media/Tools/Vegetation Options/${["TreeChild","TreeTeen","TreeAdult","TreeElderly"][i]}.svg`:`coui://uil/Standard/${i===4?"TreeDead":"TreeStump"}.svg`)}</React.Fragment>)}</div>
    <div className={base.schalterReihe}>
     <MitTooltip text={t("vegetation.onTreesKeepTheirAge")}>
      <span className={styles.schalterText}>{t("vegetation.treesDonTAge")}</span>
     </MitTooltip>
     <button role="switch" aria-label={t("vegetation.treesDonTAge")} aria-checked={options.NoAging}
      className={`${base.schalter} ${options.NoAging?base.schalterAn:""}`}
      onClick={()=>send({NoAging:!options.NoAging})}>
      <span className={`${base.schalterGriff} ${options.NoAging?base.schalterGriffAn:""}`}/>
     </button>
    </div>
    <div className={styles.label}>{t("vegetation.setsMultipleSelection")}</div>
    <div className={styles.sets}>{catalog.Sets.filter(s=>s.Species.length>0).map(s=><div key={s.Id} className={styles.set}>{button(s.Name,s.Species.every(id=>options.Species.includes(id)),()=>select(s.Species),s.Icon)}{s.Custom&&<MitTooltip text={t("vegetation.removeSetKeepPlantSelection")}><button className={styles.remove} aria-label={t("laternen.removeSet")} onClick={()=>trigger("ParkingLotTool","DeleteVegetationSet",s.Id)}>×</button></MitTooltip>}</div>)}</div>
    {button(t("vegetation.selectPlantsCustomSet"),rechts,()=>setRechts(!rechts),"coui://uil/Standard/TreesCustom.svg")}
    <div className={styles.label}>{zahl("vegetation.artenGewaehlt", options.Species.length)}</div>
    {/*
      EIN HINWEISFELD MIT FESTER HOEHE, KEINE ZEILEN, DIE AUFTAUCHEN.
      Vorher stand hier je nach Zustand nichts, eine oder zwei Zeilen - und
      das Fenster sprang bei jedem Klick auf ein Set in der Hoehe. Der
      Nutzer: *"das ist komplett bescheuert."* Er hat recht: ein Fenster,
      das seine Groesse aendert, waehrend man darin arbeitet, verschiebt
      alles unter dem Zeiger.

      Der Platz ist jetzt immer da, nur der Text wechselt.
    */}
    <div className={styles.hinweisBox}>
     {!options.Enabled
      ? t("vegetation.vegetationIsOffTheSwitch")
      : options.Species.length===0
       ? t("vegetation.chooseASetOrIndividual")
       : options.Species.some(id=>!catalog.Assets.some(a=>a.Id===id))
        ? t("vegetation.someSelectedAssetsAreMissing")
        : ""}
    </div>
   </div>

   {rechts&&<div className={styles.teilRechts}>
    <div className={styles.teilKopf}>
     <span className={styles.teilTitel}>{t("vegetation.selectPlants")}</span>
     <MitTooltip text={t("laternen.collapseTheSelection")}>
      <button className={styles.einklappen} aria-label={t("laternen.collapseTheSelection")} onClick={()=>setRechts(false)}>‹</button>
     </MitTooltip>
    </div>
    <Suchfeld wert={search} text={t("vegetation.searchPlants")} onChange={wert=>{setSearch(wert);setLimit(wert?60:gemerktesLimit);}}/>
    <div className={styles.assets} {...listenposition}>{treffer.slice(0,limit).map(a=><React.Fragment key={a.Id}>{button(a.Name,options.Species.includes(a.Id),()=>select([a.Id]),a.Icon)}</React.Fragment>)}{treffer.length===0&&<div className={base.flaechenLeer}>{t("vegetation.noPlantFound")}</div>}</div>
    {/* Immer da, nur ausgegraut: ein Knopf, der mit der Suche kommt und geht, laesst das Fenster springen. */}
    {button(t("vegetation.morePlants"),false,()=>{const mehr=limit+60;if(!search)gemerktesLimit=mehr;setLimit(mehr);},"coui://uil/Standard/Trees.svg",treffer.length<=limit)}
    <div className={styles.row}><div className={styles.setName}><Eingabefeld wert={name} text={t("laternen.setName")} maxLength={64} onChange={setName}/></div><MitTooltip text={t("vegetation.saveTheCurrentSelectionAs")}><button className={styles.choice} aria-label={t("laternen.save")} title={t("laternen.save")} disabled={!name.trim()||options.Species.length===0} onClick={()=>{trigger("ParkingLotTool","SaveVegetationSet",JSON.stringify({Name:name,Species:options.Species}));setName("");}}><img alt="" src="coui://uil/Standard/DiskSave.svg"/></button></MitTooltip></div>
   </div>}

  </div>
 </Fenster>;
};
