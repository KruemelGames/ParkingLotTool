/*
 * Attrappe fuer cs2/ui. Nur was PLT benutzt: Portal zeichnet in den Behaelter,
 * Tooltip reicht seine Kinder durch. PanelFoldout/PanelSectionRow fehlen mit
 * Absicht - dann nimmt PLT seine eigenen Bausteine (brauchbar() = false).
 */
import { createPortal } from "react-dom";

export const Portal = ({ children, container }: any) =>
  container ? createPortal(children, container) : <>{children}</>;

export const Tooltip = ({ children }: any) => <>{children}</>;

export const FloatingButton = ({ children, ...rest }: any) => <button {...rest}>{children}</button>;
