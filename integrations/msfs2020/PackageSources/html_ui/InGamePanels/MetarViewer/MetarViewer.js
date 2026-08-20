(function (root) {
    "use strict";

    var BaseElement = root.TemplateElement || root.HTMLElement;
    if (!BaseElement || !root.customElements || !root.MetarViewerPanelApp) {
        throw new Error("The MSFS toolbar runtime was not ready when METAR Viewer loaded.");
    }

    class IngamePanelMetarViewer extends BaseElement {
        constructor() {
            super();
            this.isBrowserPreview = !root.TemplateElement;
            this.ingameUi = null;
            this.app = null;
            this.initializeTimer = null;
            this.boundInitialize = this.initializePanel.bind(this);
            this.boundPanelActive = this.onPanelActive.bind(this);
            this.boundPanelInactive = this.onPanelInactive.bind(this);
        }

        connectedCallback() {
            if (BaseElement.prototype.connectedCallback) {
                BaseElement.prototype.connectedCallback.call(this);
            }

            if (this.initializeTimer !== null) {
                root.clearTimeout(this.initializeTimer);
            }
            // A custom element's connected callback may run as soon as its opening tag is
            // parsed, before the nested ingame-ui and application markup exist.
            this.initializeTimer = root.setTimeout(this.boundInitialize, 0);
        }

        initializePanel() {
            this.initializeTimer = null;
            this.disconnectPanelEvents();
            this.ingameUi = this.querySelector("ingame-ui");
            var appRoot = this.querySelector("#metar-viewer-app");
            if (!this.ingameUi || !appRoot) {
                root.console.error("METAR Viewer panel markup is incomplete.");
                return;
            }

            if (this.app) {
                this.app.destroy();
            }
            this.app = new root.MetarViewerPanelApp.MetarViewerApp(appRoot, {
                environment: root
            });
            this.ingameUi.addEventListener("panelActive", this.boundPanelActive);
            this.ingameUi.addEventListener("panelInactive", this.boundPanelInactive);
            if (this.isBrowserPreview) {
                this.app.activate();
            }
        }

        disconnectedCallback() {
            if (this.initializeTimer !== null) {
                root.clearTimeout(this.initializeTimer);
                this.initializeTimer = null;
            }
            this.disconnectPanelEvents();
            if (this.app) {
                this.app.destroy();
                this.app = null;
            }
            this.ingameUi = null;

            if (BaseElement.prototype.disconnectedCallback) {
                BaseElement.prototype.disconnectedCallback.call(this);
            }
        }

        disconnectPanelEvents() {
            if (!this.ingameUi) {
                return;
            }
            this.ingameUi.removeEventListener("panelActive", this.boundPanelActive);
            this.ingameUi.removeEventListener("panelInactive", this.boundPanelInactive);
        }

        onPanelActive() {
            if (this.app) {
                this.app.activate();
            }
        }

        onPanelInactive() {
            if (this.app) {
                this.app.deactivate();
            }
        }
    }

    if (!root.customElements.get("ingamepanel-metar-viewer")) {
        root.customElements.define("ingamepanel-metar-viewer", IngamePanelMetarViewer);
    }

    if (typeof root.checkAutoload === "function") {
        root.checkAutoload();
    }
}(typeof window !== "undefined" ? window : globalThis));
