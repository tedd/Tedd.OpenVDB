// A deterministic conceptual voxel illustration, independent of library timing or memory claims.
const svgNS = "http://www.w3.org/2000/svg";
const cells = document.getElementById("voxel-cells");
const project = (x, y, z) => [280 + (x - y) * 17, 235 + (x + y) * 8.5 - z * 19];
function face(points, fill, stroke = "#ffffff70") {
  const polygon = document.createElementNS(svgNS, "polygon");
  polygon.setAttribute("points", points.map(p => p.join(",")).join(" "));
  polygon.setAttribute("fill", fill);
  polygon.setAttribute("stroke", stroke);
  polygon.setAttribute("stroke-width", "0.6");
  cells.append(polygon);
}
function drawVolume(view) {
  cells.replaceChildren();
  const occupied = (x, y, z) => x*x + y*y + (z-3)*(z-3) < 19 && !(view !== "surface" && x >= 0 && y <= 0 && z >= 3);
  for (let z = 0; z < 8; z++) {
    for (let x = -4; x <= 4; x++) {
      for (let y = -4; y <= 4; y++) {
        if (!occupied(x,y,z)) continue;
        const cut = view !== "surface" && z >= 3 && (x === -1 && y <= 0 || y === 1 && x >= 0);
        const p = (dx,dy,dz) => project(x+dx,y+dy,z+dz);
        face([p(1,0,0),p(1,1,0),p(1,1,1),p(1,0,1)],cut ? "#ad652f" : "#075f5c");
        face([p(0,1,0),p(1,1,0),p(1,1,1),p(0,1,1)],cut ? "#cf874b" : "#088078");
        face([p(0,0,1),p(1,0,1),p(1,1,1),p(0,1,1)],cut ? "#e3ae78" : "#60b5a1");
      }
    }
  }
  if (view === "bounds") {
    const line = document.createElementNS(svgNS,"path");
    const p = (x,y,z) => project(x,y,z).join(" ");
    line.setAttribute("d",`M${p(-4,-4,0)} L${p(5,-4,0)} L${p(5,5,0)} L${p(-4,5,0)} Z M${p(-4,-4,8)} L${p(5,-4,8)} L${p(5,5,8)} L${p(-4,5,8)} Z M${p(-4,-4,0)} L${p(-4,-4,8)} M${p(5,-4,0)} L${p(5,-4,8)} M${p(5,5,0)} L${p(5,5,8)} M${p(-4,5,0)} L${p(-4,5,8)}`);
    line.setAttribute("fill","none");line.setAttribute("stroke","#658c80");line.setAttribute("stroke-dasharray","4 4");cells.append(line);
  }
  document.getElementById("volume-description").textContent = view === "surface" ? "An illustrative sphere of teal voxels, shown without the cutaway." : view === "bounds" ? "A cutaway voxel sphere inside dashed bounds, showing surrounding empty space." : "An illustrative cutaway sphere built from teal voxels. Empty space needs no individual voxel storage.";
}
drawVolume("cutaway");
document.querySelectorAll("[data-view]").forEach(button => button.addEventListener("click", () => {
  document.querySelectorAll("[data-view]").forEach(other => other.setAttribute("aria-pressed", String(other === button)));
  drawVolume(button.dataset.view);
}));
document.querySelectorAll("[data-copy]").forEach(button => button.addEventListener("click", async () => {
  try {
    await navigator.clipboard.writeText(button.dataset.copy);
    button.textContent = "Copied";
    document.getElementById("copy-status").textContent = "Installation command copied.";
  } catch {
    const selection = window.getSelection();
    const range = document.createRange();
    range.selectNodeContents(button.previousElementSibling);
    selection.removeAllRanges(); selection.addRange(range);
    button.textContent = "Selected";
    document.getElementById("copy-status").textContent = "Installation command selected. Copy it using your keyboard.";
  }
  window.setTimeout(() => {button.textContent = "Copy";}, 1800);
}));
