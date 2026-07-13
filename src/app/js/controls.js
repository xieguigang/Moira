/* controls.js — minimal OrbitControls (rotate / zoom / pan), no dependency */
(function (global) {
  'use strict';

  class OrbitControls {
    constructor(camera, dom) {
      this.camera = camera;
      this.dom = dom;
      this.target = new THREE.Vector3(0, 0, 0);
      this.enableDamping = false;
      this.minDistance = 1;
      this.maxDistance = 5000;

      const offset = new THREE.Vector3().subVectors(camera.position, this.target);
      this.spherical = new THREE.Spherical().setFromVector3(offset);
      this.sphericalDelta = new THREE.Spherical(0, 0, 0);
      this.panOffset = new THREE.Vector3();
      this.scale = 1;

      this._state = 'none';
      this._px = 0; this._py = 0;
      this._bind();
    }

    _bind() {
      const dom = this.dom;
      dom.style.touchAction = 'none';
      dom.addEventListener('mousedown', (e) => this._onDown(e));
      window.addEventListener('mousemove', (e) => this._onMove(e));
      window.addEventListener('mouseup', () => this._onUp());
      dom.addEventListener('wheel', (e) => this._onWheel(e), { passive: false });
      dom.addEventListener('contextmenu', (e) => e.preventDefault());
    }

    _onDown(e) {
      this._state = (e.button === 2 || e.shiftKey) ? 'pan' : 'rotate';
      this._px = e.clientX; this._py = e.clientY;
    }
    _onMove(e) {
      if (this._state === 'none') return;
      const dx = e.clientX - this._px;
      const dy = e.clientY - this._py;
      this._px = e.clientX; this._py = e.clientY;
      const h = this.dom.clientHeight || 1;
      if (this._state === 'rotate') {
        this.sphericalDelta.theta -= 2 * Math.PI * dx / h;
        this.sphericalDelta.phi -= 2 * Math.PI * dy / h;
      } else {
        this._pan(dx, dy);
      }
    }
    _onUp() { this._state = 'none'; }
    _onWheel(e) {
      e.preventDefault();
      const f = e.deltaY < 0 ? 0.92 : 1.08;
      this.scale *= f;
    }

    _pan(dx, dy) {
      const dist = this.spherical.radius;
      const h = this.dom.clientHeight || 1;
      const targetDistance = 2 * dist * Math.tan((this.camera.fov / 2) * Math.PI / 180) / h;
      const v = new THREE.Vector3(-dx * targetDistance, dy * targetDistance, 0);
      v.applyQuaternion(this.camera.quaternion);
      this.panOffset.add(v);
    }

    update() {
      const offset = new THREE.Vector3().subVectors(this.camera.position, this.target);
      const sph = new THREE.Spherical().setFromVector3(offset);
      sph.theta += this.sphericalDelta.theta;
      sph.phi += this.sphericalDelta.phi;
      sph.phi = Math.max(0.05, Math.min(Math.PI - 0.05, sph.phi));
      sph.radius *= this.scale;
      sph.radius = Math.max(this.minDistance, Math.min(this.maxDistance, sph.radius));

      offset.setFromSpherical(sph);
      this.target.add(this.panOffset);
      this.camera.position.copy(this.target).add(offset);
      this.camera.lookAt(this.target);

      this.sphericalDelta.set(0, 0, 0);
      this.panOffset.set(0, 0, 0);
      this.scale = 1;
    }
  }

  global.OrbitControls = OrbitControls;
})(window);
