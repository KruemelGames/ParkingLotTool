import React, { useEffect, useState } from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { useTexte } from "./texte";
import { Slider, MitTooltip, Fenster, Eingabefeld, SettingActions } from "./controls";
import styles from "./vegetation.module.scss";
import eigene from "./laternen.module.scss";
import base from "./panel.module.scss";

const STANDARD = '{"Enabled":true,"Abstand":30,"Set":"strasse","Einzeln":"StreetlightSingle02","Doppelt":"StreetlightDouble02"}';
const value$ = bindValue<string>("ParkingLotTool", "Laternen", STANDARD);
const default$ = bindValue<string>("ParkingLotTool", "LaternenDefault", STANDARD);
const katalog$ = bindValue<string>("ParkingLotTool", "LaternenKatalog", '{"Modelle":[],"Sets":[]}');
const lichtkreise$ = bindValue<boolean>("ParkingLotTool", "LaternenLichtkreise", false);
type Optionen = {Enabled:boolean;Abstand:number;Set:string;Einzeln:string;Doppelt:string};
type Modell = {Id:string;Name:string;Icon:string;Bauart:string};
type LSet = {Id:string;Name:string;Einzeln:string;Doppelt:string;Custom:boolean};

/**
 * DAS LATERNENFENSTER (Nutzer 2026-10-04).
 *
 * Wie Vegetation und an derselben Stelle; das eine schliesst das andere
 * (panel.tsx haelt EIN Seitenfenster). Kein Alter, kein Muster: drei feste
 * Sets und eigene Sets aus genau einem Modell je Feld "Einzeln" und
 * "Doppelt", dazu der Abstand. Die Felder tragen dasselbe Zeichen wie die
 * Vorschau: ein Arm fuer Einzeln, zwei fuer Doppelt.
 */
const useLaternen = () => {
 const texte=useTexte();
 const t=texte.text;
 const optionen:Optionen=JSON.parse(useValue(value$));
 const katalog:{Modelle:Modell[];Sets:LSet[]}=JSON.parse(useValue(katalog$));
 const senden=(patch:Partial<Optionen>)=>trigger("ParkingLotTool","SetLaternen",JSON.stringify({...optionen,...patch}));
 const name=(id:string)=>katalog.Modelle.find(m=>m.Id===id)?.Name??id;
 return {t,optionen,katalog,senden,name};
};

/** Das Zeichen aus der Vorschau: Mast mit einem oder zwei Armen. */
const Zeichen = ({ doppelt }: { doppelt: boolean }) =>
 <span className={eigene.zeichen}>
  {doppelt ? <span className={`${eigene.arm} ${eigene.armLinks} ${eigene.doppelt}`}/> : null}
  <span className={`${eigene.arm} ${eigene.armRechts} ${doppelt ? eigene.doppelt : eigene.einzeln}`}/>
  <span className={`${eigene.mast} ${doppelt ? eigene.doppelt : eigene.einzeln}`}/>
 </span>;

/** Bild des Modells, ohne Bild sein Name - ein leerer Knopf sagte nichts. */
const ModellKnopf = ({ modell, gewaehlt, onClick }: { modell: Modell; gewaehlt: boolean; onClick: () => void }) =>
 <MitTooltip text={modell.Name}>
  <button className={`${styles.choice} ${gewaehlt?styles.selected:""}`} aria-pressed={gewaehlt} aria-label={modell.Name} onClick={onClick}>
   {modell.Icon ? <img alt="" src={modell.Icon}/> : <span className={eigene.ohneBild}>{modell.Name}</span>}
  </button>
 </MitTooltip>;

/** Im Panel unter "Roads": Name (oeffnet das Fenster) und Schalter - wie Vegetation. */
export const LaternenSchalter = ({ onOeffnen }: { onOeffnen: (an: boolean) => void }) => {
 const {t,optionen,senden}=useLaternen();
 useEffect(()=>{trigger("ParkingLotTool","RefreshLaternen");},[]);
 return <div className={styles.root}>
  <div className={base.schalterReihe}>
   <MitTooltip text={t("laternen.openTheLanternSettings")}>
    <button className={styles.nameKnopf} onClick={()=>onOeffnen(true)}><span className={base.label}>{t("laternen.lanterns")}</span></button>
   </MitTooltip>
   <MitTooltip text={t("laternen.placeStreetLanternsAlongThe")}>
    <button role="switch" aria-label={t("laternen.lanterns")} aria-checked={optionen.Enabled}
     className={`${base.schalter} ${optionen.Enabled?base.schalterAn:""}`}
     onClick={()=>{const an=!optionen.Enabled;senden({Enabled:an});onOeffnen(an);}}>
     <span className={`${base.schalterGriff} ${optionen.Enabled?base.schalterGriffAn:""}`}/>
    </button>
   </MitTooltip>
  </div>
 </div>;
};

const BREITE_LINKS = 300;
const BREITE_RECHTS = 340;

export const LaternenFenster = ({ pos, onPos, onClose }: {
 pos?: {x:number;y:number};
 onPos?: (p:{x:number;y:number})=>void;
 onClose: () => void;
}) => {
 const {t,optionen,katalog,senden,name}=useLaternen();
 const [rechts,setRechts]=useState(false);
 const [setName,setSetName]=useState("");
 const standard:Optionen=JSON.parse(useValue(default$));
 const lichtkreise=useValue(lichtkreise$);
 const vergleich=(o:Optionen)=>JSON.stringify([o.Enabled,o.Abstand,o.Einzeln,o.Doppelt]);
 const abweichend=vergleich(optionen)!==vergleich(standard);
 const fehlt=[optionen.Einzeln,optionen.Doppelt].some(id=>katalog.Modelle.length>0&&!katalog.Modelle.some(m=>m.Id===id&&m.Icon!==""));
 const feld=(doppelt:boolean)=>{
  const gewaehlt=doppelt?optionen.Doppelt:optionen.Einzeln;
  return <div className={eigene.feld}>
   <div className={eigene.feldKopf}><Zeichen doppelt={doppelt}/><span className={styles.label}>{doppelt?t("laternen.double"):t("laternen.single")}</span></div>
   <div className={eigene.modelle}>{katalog.Modelle.map(m=><React.Fragment key={m.Id}>
    <ModellKnopf modell={m} gewaehlt={gewaehlt===m.Id} onClick={()=>senden(doppelt?{Doppelt:m.Id}:{Einzeln:m.Id})}/>
   </React.Fragment>)}</div>
  </div>;
 };
 return <Fenster titel={t("laternen.lanterns")}
   breite={rechts?BREITE_LINKS+BREITE_RECHTS:BREITE_LINKS}
   pos={pos} onPos={onPos} vonUnten onClose={onClose}>
  <div className={styles.zweiTeile}>

   <div className={styles.teilLinks}>
    <div className={styles.row}>
     <div className={styles.label} style={{flex:"1 1 0"}}>{t("laternen.defaultForTheWholeWindow")}</div>
     <SettingActions label={t("laternen.lanterns")} active={abweichend}
      onReset={()=>trigger("ParkingLotTool","ResetLaternen")}
      onSetDefault={()=>trigger("ParkingLotTool","SaveLaternenDefault")}/>
    </div>
    <Slider label={t("laternen.spacing")} tooltip={t("laternen.largestGapBetweenTwoLanterns")}
     value={optionen.Abstand} min={20} max={40} step={1} digits={0} unit={t("einheit.meter")} ton="Fahrwege" onChange={Abstand=>senden({Abstand})}/>
    <div className={base.schalterReihe}>
     <MitTooltip text={t("laternen.showsInThePreviewHow")}>
      <span className={styles.schalterText}>{t("laternen.showLightRange")}</span>
     </MitTooltip>
     <button role="switch" aria-label={t("laternen.showLightRange")} aria-checked={lichtkreise}
      className={`${base.schalter} ${lichtkreise?base.schalterAn:""}`}
      onClick={()=>trigger("ParkingLotTool","SetLaternenLichtkreise",!lichtkreise)}>
      <span className={`${base.schalterGriff} ${lichtkreise?base.schalterGriffAn:""}`}/>
     </button>
    </div>
    <div className={styles.label}>{t("laternen.sets")}</div>
    <div className={styles.sets}>{katalog.Sets.map(s=><div key={s.Id} className={styles.set}>
     <MitTooltip text={t("laternen.setTooltip", { name: s.Name, einzeln: name(s.Einzeln), doppelt: name(s.Doppelt) })}>
      <button className={`${styles.choice} ${eigene.setKnopf} ${optionen.Set===s.Id?styles.selected:""}`} aria-pressed={optionen.Set===s.Id}
       aria-label={s.Name} onClick={()=>senden({Einzeln:s.Einzeln,Doppelt:s.Doppelt})}>
       <span className={eigene.setName}>{s.Name}</span>
      </button>
     </MitTooltip>
     {s.Custom&&<MitTooltip text={t("laternen.removeSet")}><button className={styles.remove} aria-label={t("laternen.removeSet")} onClick={()=>trigger("ParkingLotTool","DeleteLaternenSet",s.Id)}>×</button></MitTooltip>}
    </div>)}</div>
    <MitTooltip text={t("laternen.chooseModelsCustomSet")}>
     <button className={`${styles.choice} ${eigene.breiterKnopf} ${rechts?styles.selected:""}`} aria-pressed={rechts} onClick={()=>setRechts(!rechts)}>
      <span>{t("laternen.chooseModelsCustomSet")}</span>
     </button>
    </MitTooltip>
    <div className={eigene.wahl}><Zeichen doppelt={false}/><span className={styles.label}>{name(optionen.Einzeln)}</span></div>
    <div className={eigene.wahl}><Zeichen doppelt={true}/><span className={styles.label}>{name(optionen.Doppelt)}</span></div>
    {/* Feste Hoehe, nur der Text wechselt - das Fenster springt nicht. */}
    <div className={styles.hinweisBox}>
     {!optionen.Enabled
      ? t("laternen.lanternsAreOffTheSwitch")
      : fehlt
       ? t("laternen.aSelectedModelIsMissing")
       : optionen.Set===""
        ? t("laternen.ownChoiceSaveItAs")
        : ""}
    </div>
   </div>

   {rechts&&<div className={styles.teilRechts}>
    <div className={styles.teilKopf}>
     <span className={styles.teilTitel}>{t("laternen.chooseModels")}</span>
     <MitTooltip text={t("laternen.collapseTheSelection")}>
      <button className={styles.einklappen} aria-label={t("laternen.collapseTheSelection")} onClick={()=>setRechts(false)}>‹</button>
     </MitTooltip>
    </div>
    {feld(false)}
    {feld(true)}
    <div className={styles.row}><div className={styles.setName}><Eingabefeld wert={setName} text={t("laternen.setName")} maxLength={64} onChange={setSetName}/></div>
     <MitTooltip text={t("laternen.saveSingleAndDoubleAs")}>
      <button className={styles.choice} aria-label={t("laternen.save")} disabled={!setName.trim()}
       onClick={()=>{trigger("ParkingLotTool","SaveLaternenSet",JSON.stringify({Name:setName,Einzeln:optionen.Einzeln,Doppelt:optionen.Doppelt}));setSetName("");}}>
       <img alt="" src="coui://uil/Standard/DiskSave.svg"/>
      </button>
     </MitTooltip>
    </div>
   </div>}

  </div>
 </Fenster>;
};
