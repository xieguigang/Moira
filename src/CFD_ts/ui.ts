import { mobile } from "./global";
import { four9ths, one36th, one9th, options, uiAdapter } from "./options";

/**
 * this html user interface handler
*/
export class ui implements uiAdapter {

    public readonly canvas: HTMLCanvasElement;
    public readonly context: CanvasRenderingContext2D;
    public readonly image: ImageData;

    public readonly speedSlider: HTMLInputElement;
    public readonly stepsSlider: HTMLInputElement;
    public readonly startButton: HTMLInputElement;

    public readonly speedValue: HTMLInputElement;
    public readonly viscSlider: HTMLInputElement;
    public readonly viscValue: HTMLInputElement;
    public readonly mouseSelect: HTMLSelectElement;

    public readonly plotSelect: HTMLSelectElement;
    public readonly contrastSlider: HTMLInputElement;
    public readonly pixelCheck: HTMLInputElement;
    public readonly tracerCheck: HTMLInputElement;
    public readonly flowlineCheck: HTMLInputElement;
    public readonly forceCheck: HTMLInputElement;
    public readonly sensorCheck: HTMLInputElement;
    public readonly dataCheck: HTMLInputElement;
    public readonly rafCheck: HTMLInputElement;
    public readonly speedReadout: HTMLElement;
    public readonly dataSection: HTMLElement;
    public readonly dataArea: HTMLElement;
    public readonly dataButton: HTMLInputElement;

    public readonly sizeSelect: HTMLSelectElement;

    private draggingSensor = false;
    private mouseIsDown = false;

    public get pxPerSquare(): number {
        var i = this.sizeSelect.selectedIndex;
        var size = this.sizeSelect.options[i].value;

        return Number(size);
    };

    // width of plotted grid site in pixels
    // grid dimensions for simulation
    public get xdim(): number {
        return this.canvas.width / this.pxPerSquare;
    }

    public get ydim(): number {
        return this.canvas.height / this.pxPerSquare;
    }

    constructor(private opts: options,
        canvas_id: string = "theCanvas",
        speedSlider: string = "speedSlider",
        stepsSlider: string = "stepsSlider",
        startButton: string = "startButton",
        speedValue: string = "speedValue",
        viscSlider: string = "viscSlider",
        viscValue: string = "viscValue",
        mouseSelect: string = "mouseSelect",
        plotSelect: string = "plotSelect",
        contrastSlider: string = "contrastSlider",
        pixelCheck: string = "pixelCheck",
        tracerCheck: string = "tracerCheck",
        flowlineCheck: string = "flowlineCheck",
        forceCheck: string = "forceCheck",
        sensorCheck: string = "sensorCheck",
        dataCheck: string = "dataCheck",
        rafCheck: string = "rafCheck",
        speedReadout: string = "speedReadout",
        dataSection: string = "dataSection",
        dataArea: string = "dataArea",
        dataButton: string = "dataButton",
        sizeSelect: string = "sizeSelect") {

        const canvas: HTMLCanvasElement = <any>document.getElementById(canvas_id);
        const context: CanvasRenderingContext2D = <any>canvas.getContext('2d');
        // for direct pixel manipulation (faster than fillRect)
        const image: ImageData = context.createImageData(canvas.width, canvas.height);

        // set all alpha values to opaque
        for (var i = 3; i < image.data.length; i += 4) {
            image.data[i] = 255;
        }

        this.canvas = canvas;
        this.context = context;
        this.image = image;

        this.speedSlider = <any>document.getElementById(speedSlider);
        this.stepsSlider = <any>document.getElementById(stepsSlider);
        this.startButton = <any>document.getElementById(startButton);
        this.speedValue = <any>document.getElementById(speedValue);
        this.viscSlider = <any>document.getElementById(viscSlider);
        this.viscValue = <any>document.getElementById(viscValue);
        this.mouseSelect = <any>document.getElementById(mouseSelect);

        this.plotSelect = <any>document.getElementById(plotSelect);
        this.contrastSlider = <any>document.getElementById(contrastSlider);
        this.pixelCheck = <any>document.getElementById(pixelCheck);
        this.tracerCheck = <any>document.getElementById(tracerCheck);
        this.flowlineCheck = <any>document.getElementById(flowlineCheck);
        this.forceCheck = <any>document.getElementById(forceCheck);
        this.sensorCheck = <any>document.getElementById(sensorCheck);
        this.dataCheck = <any>document.getElementById(dataCheck);
        this.rafCheck = <any>document.getElementById(rafCheck);
        this.speedReadout = <any>document.getElementById(speedReadout);
        this.dataSection = <any>document.getElementById(dataSection);
        this.dataArea = <any>document.getElementById(dataArea);
        this.dataButton = <any>document.getElementById(dataButton);

        this.sizeSelect = <any>document.getElementById(sizeSelect);
        this.sizeSelect.selectedIndex = 5;

        // smaller works better on mobile platforms
        if (mobile) {
            this.sizeSelect.selectedIndex = 1;
        }

        this.setEvents();
    }
    public get speed(): number {
        return Number(this.speedSlider.value);
    }

    public get drawTracers(): boolean {
        return (this.tracerCheck.checked);
    }

    public get drawFlowlines(): boolean {
        return (this.flowlineCheck.checked);
    }

    public get drawForceArrow(): boolean {
        return (this.forceCheck.checked);
    }

    public get drawSensor(): boolean {
        return (this.sensorCheck.checked);
    }

    public get plotType(): number {
        return this.plotSelect.selectedIndex;
    }

    public get viscosity(): number {
        return Number(this.viscSlider.value);
    }

    public get contrast(): number {
        return Number(this.contrastSlider.value);
    }

    private setEvents() {
        this.canvas.addEventListener('mousedown', (e) => this.mouseDown(e), false);
        this.canvas.addEventListener('mousemove', (e) => this.mouseMove(e), false);
        this.canvas.addEventListener('touchstart', (e) => this.mouseDown(e), false);
        this.canvas.addEventListener('touchmove', (e) => this.mouseMove(e), false);

        document.body.addEventListener('mouseup', (e) => this.mouseUp(e), false);	// button release could occur outside canvas
        document.body.addEventListener('touchend', (e) => this.mouseUp(e), false);
    }

    // Set the fluid variables at the boundaries, according to the current slider value:
    setBoundaries() {
        const u0 = Number(this.speedSlider.value);
        const xdim = this.xdim;
        const ydim = this.ydim;

        for (var x = 0; x < xdim; x++) {
            setEquil(x, 0, u0, 0, 1);
            setEquil(x, ydim - 1, u0, 0, 1);
        }
        for (var y = 1; y < ydim - 1; y++) {
            setEquil(0, y, u0, 0, 1);
            setEquil(xdim - 1, y, u0, 0, 1);
        }
    }

    // Move the tracer particles:
    moveTracers() {
        const xdim = this.xdim;
        const ydim = this.ydim;
        const tracerX = this.opts.tracerX;
        const tracerY = this.opts.tracerY;

        for (var t = 0; t < this.opts.nTracers; t++) {
            var roundedX = Math.round(tracerX[t]);
            var roundedY = Math.round(tracerY[t]);
            var index = roundedX + roundedY * xdim;
            tracerX[t] += ux[index];
            tracerY[t] += uy[index];
            if (tracerX[t] > xdim - 1) {
                tracerX[t] = 0;
                tracerY[t] = Math.random() * ydim;
            }
        }
    }

    // "Drag" the fluid in a direction determined by the mouse (or touch) motion:
    // (The drag affects a "circle", 5 px in diameter, centered on the given coordinates.)
    push(pushX: number, pushY: number, pushUX: number, pushUY: number) {
        // First make sure we're not too close to edge:
        var margin = 3;
        if ((pushX > margin) && (pushX < this.xdim - 1 - margin) && (pushY > margin) && (pushY < this.ydim - 1 - margin)) {
            for (var dx = -1; dx <= 1; dx++) {
                setEquil(pushX + dx, pushY + 2, pushUX, pushUY);
                setEquil(pushX + dx, pushY - 2, pushUX, pushUY);
            }
            for (var dx = -2; dx <= 2; dx++) {
                for (var dy = -1; dy <= 1; dy++) {
                    setEquil(pushX + dx, pushY + dy, pushUX, pushUY);
                }
            }
        }
    }

    // Set all densities in a cell to their equilibrium values for a given velocity and density:
    // (If density is omitted, it's left unchanged.)
    setEquil(x: number, y: number, newux: number, newuy: number, newrho?: number) {
        var i = x + y * this.xdim;
        if (typeof newrho == 'undefined') {
            newrho = rho[i];
        }
        var ux3 = 3 * newux;
        var uy3 = 3 * newuy;
        var ux2 = newux * newux;
        var uy2 = newuy * newuy;
        var uxuy2 = 2 * newux * newuy;
        var u2 = ux2 + uy2;
        var u215 = 1.5 * u2;

        n0[i] = four9ths * newrho * (1 - u215);
        nE[i] = one9th * newrho * (1 + ux3 + 4.5 * ux2 - u215);
        nW[i] = one9th * newrho * (1 - ux3 + 4.5 * ux2 - u215);
        nN[i] = one9th * newrho * (1 + uy3 + 4.5 * uy2 - u215);
        nS[i] = one9th * newrho * (1 - uy3 + 4.5 * uy2 - u215);
        nNE[i] = one36th * newrho * (1 + ux3 + uy3 + 4.5 * (u2 + uxuy2) - u215);
        nSE[i] = one36th * newrho * (1 + ux3 - uy3 + 4.5 * (u2 - uxuy2) - u215);
        nNW[i] = one36th * newrho * (1 - ux3 + uy3 + 4.5 * (u2 - uxuy2) - u215);
        nSW[i] = one36th * newrho * (1 - ux3 - uy3 + 4.5 * (u2 + uxuy2) - u215);
        rho[i] = newrho;
        ux[i] = newux;
        uy[i] = newuy;
    }

    /**
     * Functions to handle mouse/touch interaction 
    */
    mouseDown(e) {
        if (this.sensorCheck.checked) {
            const pxPerSquare = this.pxPerSquare;

            var canvasLoc = pageToCanvas(e.pageX, e.pageY);
            var gridLoc = canvasToGrid(canvasLoc.x, canvasLoc.y);
            var dx = (gridLoc.x - this.opts.sensorX) * pxPerSquare;
            var dy = (gridLoc.y - this.opts.sensorY) * pxPerSquare;

            if (Math.sqrt(dx * dx + dy * dy) <= 8) {
                this.draggingSensor = true;
            }
        }

        this.mousePressDrag(e);
    };
    mouseMove(e) {
        if (this.mouseIsDown) {
            this.mousePressDrag(e);
        }
    };
    mouseUp(e) {
        this.mouseIsDown = false;
        this.draggingSensor = false;
    };

    // Handle mouse press or drag:
    mousePressDrag(e) {
        e.preventDefault();
        this.mouseIsDown = true;
        var canvasLoc = pageToCanvas(e.pageX, e.pageY);
        if (draggingSensor) {
            var gridLoc = canvasToGrid(canvasLoc.x, canvasLoc.y);
            this.opts.sensorX = gridLoc.x;
            this.opts.sensorY = gridLoc.y;
            paintCanvas();
            return;
        }
        if (mouseSelect.selectedIndex == 2) {
            this.opts.mouseX = canvasLoc.x;
            this.opts.mouseY = canvasLoc.y;
            return;
        }
        var gridLoc = canvasToGrid(canvasLoc.x, canvasLoc.y);
        if (this.mouseSelect.selectedIndex == 0) {
            addBarrier(gridLoc.x, gridLoc.y);
            paintCanvas();
        } else {
            removeBarrier(gridLoc.x, gridLoc.y);
        }
    }

    // Convert page coordinates to canvas coordinates:
    pageToCanvas(pageX, pageY) {
        var canvasX = pageX - canvas.offsetLeft;
        var canvasY = pageY - canvas.offsetTop;
        // this simple subtraction may not work when the canvas is nested in other elements
        return { x: canvasX, y: canvasY };
    }

    // Convert canvas coordinates to grid coordinates:
    canvasToGrid(canvasX, canvasY) {
        var gridX = Math.floor(canvasX / pxPerSquare);
        var gridY = Math.floor((canvas.height - 1 - canvasY) / pxPerSquare); 	// off by 1?
        return { x: gridX, y: gridY };
    }

    // Add a barrier at a given grid coordinate location:
    addBarrier(x, y) {
        if ((x > 1) && (x < xdim - 2) && (y > 1) && (y < ydim - 2)) {
            barrier[x + y * xdim] = true;
        }
    }

    // Remove a barrier at a given grid coordinate location:
    removeBarrier(x, y) {
        if (barrier[x + y * xdim]) {
            barrier[x + y * xdim] = false;
            paintCanvas();
        }
    }

    // Clear all barriers:
    clearBarriers() {
        for (var x = 0; x < xdim; x++) {
            for (var y = 0; y < ydim; y++) {
                barrier[x + y * xdim] = false;
            }
        }
        paintCanvas();
    }

    // Function to start or pause the simulation:
    startStop() {
        running = !running;
        if (running) {
            startButton.value = "Pause";
            resetTimer();
            CFD_app.simulate();
        } else {
            startButton.value = " Run ";
        }
    }

    // Reset the timer that handles performance evaluation:
    resetTimer() {
        this.opts.stepCount = 0;
        this.opts.startTime = (new Date()).getTime();
    }

    // Show value of flow speed setting:
    adjustSpeed() {
        speedValue.innerHTML = Number(speedSlider.value).toFixed(3);
    }

    // Show value of viscosity:
    adjustViscosity() {
        viscValue.innerHTML = Number(viscSlider.value).toFixed(3);
    }

    // Show or hide the data area:
    showData() {
        if (this.dataCheck.checked) {
            this.dataSection.style.display = "block";
        } else {
            this.dataSection.style.display = "none";
        }
    }

    // Start or stop collecting data:
    startOrStopData() {
        this.opts.collectingData = !this.opts.collectingData;
        if (this.opts.collectingData) {
            this.opts.time = 0;
            this.dataArea.innerHTML = "Time \tDensity\tVel_x \tVel_y \tForce_x\tForce_y\n";
            this.writeData();
            this.dataButton.value = "Stop data collection";
            this.opts.showingPeriod = false;
            this.periodButton.value = "Show F_y period";
        } else {
            this.dataButton.value = "Start data collection";
        }
    }

    // Write one line of data to the data area:
    writeData() {
        var timeString = String(this.opts.time);
        var xdim = this.xdim;
        while (timeString.length < 5) timeString = "0" + timeString;
        var sIndex = this.opts.sensorX + this.opts.sensorY * xdim;
        this.dataArea.innerHTML += timeString + "\t" + Number(rho[sIndex]).toFixed(4) + "\t"
            + Number(ux[sIndex]).toFixed(4) + "\t" + Number(uy[sIndex]).toFixed(4) + "\t"
            + Number(this.opts.barrierFx).toFixed(4) + "\t" + Number(this.opts.barrierFy).toFixed(4) + "\n";
        this.dataArea.scrollTop = this.dataArea.scrollHeight;
    }

    // Handle click to "show period" button
    showPeriod() {
        this.opts.showingPeriod = !this.opts.showingPeriod;

        if (this.opts.showingPeriod) {
            this.opts.time = 0;
            this.opts.lastBarrierFy = 1.0;	// arbitrary positive value
            this.opts.lastFyOscTime = -1.0;	// arbitrary negative value
            this.dataArea.innerHTML = "Period of F_y oscillation\n";
            this.periodButton.value = "Stop data";
            this.opts.collectingData = false;
            this.dataButton.value = "Start data collection";
        } else {
            this.periodButton.value = "Show F_y period";
        }
    }

    // Write all the barrier locations to the data area:
    showBarrierLocations() {
        const dataArea = this.dataArea;
        const xdim = this.xdim;
        const ydim = this.ydim;

        dataArea.innerHTML = '{name:"Barrier locations",\n';
        dataArea.innerHTML += 'locations:[\n';
        for (var y = 1; y < ydim - 1; y++) {
            for (var x = 1; x < xdim - 1; x++) {
                if (barrier[x + y * xdim]) dataArea.innerHTML += x + ',' + y + ',\n';
            }
        }
        dataArea.innerHTML = dataArea.innerHTML.substr(0, dataArea.innerHTML.length - 2); // remove final comma
        dataArea.innerHTML += '\n]},\n';
    }

    // Place a preset barrier:
    placePresetBarrier() {
        var index = barrierSelect.selectedIndex;
        if (index == 0) return;
        this.clearBarriers();
        var bCount = barrierList[index - 1].locations.length / 2;	// number of barrier sites
        // To decide where to place it, find minimum x and min/max y:
        var xMin = barrierList[index - 1].locations[0];
        var yMin = barrierList[index - 1].locations[1];
        var yMax = yMin;
        for (var siteIndex = 2; siteIndex < 2 * bCount; siteIndex += 2) {
            if (barrierList[index - 1].locations[siteIndex] < xMin) {
                xMin = barrierList[index - 1].locations[siteIndex];
            }
            if (barrierList[index - 1].locations[siteIndex + 1] < yMin) {
                yMin = barrierList[index - 1].locations[siteIndex + 1];
            }
            if (barrierList[index - 1].locations[siteIndex + 1] > yMax) {
                yMax = barrierList[index - 1].locations[siteIndex + 1];
            }
        }
        var yAverage = Math.round((yMin + yMax) / 2);
        // Now place the barriers:
        for (var siteIndex = 0; siteIndex < 2 * bCount; siteIndex += 2) {
            var x = barrierList[index - 1].locations[siteIndex] - xMin + Math.round(ydim / 3);
            var y = barrierList[index - 1].locations[siteIndex + 1] - yAverage + Math.round(ydim / 2);
            addBarrier(x, y);
        }
        paintCanvas();
        barrierSelect.selectedIndex = 0;	// A choice on this menu is a one-time action, not an ongoing setting
    }

    // Print debugging data:
    debug() {
        const tracerX = this.opts.tracerX;
        const tracerY = this.opts.tracerY;

        this.dataArea.innerHTML = "Tracer locations:\n";

        for (var t = 0; t < this.opts.nTracers; t++) {
            this.dataArea.innerHTML += tracerX[t] + ", " + tracerY[t] + "\n";
        }
    }
}