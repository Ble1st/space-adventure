// Restenergie eines zerstörten Jumpgates (Materialien "Glow"/"Signal" in gate_destroyed.glb): sehr dunkle Glut, dazu
// unregelmäßig aufblitzende Funken. Zufall aus der Modellposition (Zellen) und der Zeit — ohne Texturen, mobil tauglich.
shader_type spatial;
render_mode cull_disabled;

uniform vec3 base_color : source_color = vec3(0.06, 0.05, 0.05);
uniform vec3 ember_color : source_color = vec3(1.0, 0.35, 0.08);
uniform float glow = 0.25;     // ständige Glut
uniform float sparks = 6.0;    // Stärke der Funken
uniform float cell = 9.0;      // Zellen je Modelleinheit

varying vec3 lp;

void vertex() {
	lp = VERTEX;
}

float hash(vec3 q) {
	q = fract(q * 0.3183099 + vec3(0.11, 0.17, 0.13));
	q *= 17.0;
	return fract(q.x * q.y * q.z * (q.x + q.y + q.z));
}

void fragment() {
	vec3 c = floor(lp * cell);
	float seed = hash(c);
	float rate = 1.5 + 4.0 * seed;
	float t = TIME * rate + seed * 37.0;
	float n = hash(c + vec3(floor(t), 0.0, 0.0));
	float spark = pow(n, 9.0) * (1.0 - fract(t));          // seltenes, kurzes Aufblitzen mit Abklingen
	float smoulder = 0.6 + 0.4 * sin(TIME * (0.7 + seed) + seed * 6.2831);
	ALBEDO = base_color;
	ROUGHNESS = 0.9;
	EMISSION = ember_color * (glow * smoulder * (0.3 + 0.7 * seed) + sparks * spark);
}
