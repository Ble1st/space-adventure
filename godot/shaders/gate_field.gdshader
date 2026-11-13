// Durchflugfeld der Jumpgates (Material "Field" in gate.glb): unbeleuchtet, additiv, ohne Texturen.
// Ortsangabe aus der Modell-Ebene (Öffnung in XY, Radius 1), damit die Muster mit dem Tor skalieren.
// mode: 0 offen (ruhiger Puls, wandernde Wellenringe), 1 eingeschränkt (Warnstreifen, langsamer Warnpuls),
//       2 geschlossen (Feld aus, nur ein schwaches stehendes Sperrgitter mit Kreuz).
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec3 field_color : source_color = vec3(0.35, 0.9, 1.0);
uniform vec3 rim_color : source_color = vec3(0.25, 0.3, 1.0);
uniform float intensity = 1.0;
uniform int mode = 0;
uniform float deny = 0.0;      // 1 = für dich gesperrt (eingeschränkt, Streifen dichter und röter)
uniform float radius = 0.95;   // Feldradius in Modelleinheiten

varying vec2 p;

void vertex() {
	p = VERTEX.xy;
}

float band(float x, float w) {
	// 1 auf einer Linie bei x = 0 (periodisch), weich ausgeblendet über w
	float d = abs(fract(x) - 0.5);
	return 1.0 - smoothstep(0.5 - w, 0.5, d);
}

void fragment() {
	float r = clamp(length(p) / radius, 0.0, 1.2);
	float edge = 1.0 - smoothstep(0.9, 1.0, r);          // weicher Rand
	float rimGlow = smoothstep(0.55, 0.98, r) * edge;     // heller Kranz innen am Ring
	vec3 col = mix(field_color, rim_color, smoothstep(0.0, 1.0, r));
	float e;
	if (mode == 0) {
		float waves = 0.5 + 0.5 * sin(r * 18.0 - TIME * 3.2);
		float pulse = 0.85 + 0.15 * sin(TIME * 1.7);
		float shimmer = 0.92 + 0.08 * sin(p.x * 23.0 + TIME * 5.1) * sin(p.y * 19.0 - TIME * 4.3);
		e = (0.30 + 0.30 * waves * (1.0 - r) + 0.55 * rimGlow) * pulse * shimmer;
	} else if (mode == 1) {
		float density = mix(3.5, 5.5, deny);
		float stripes = band((p.x + p.y) * density + TIME * 0.35, 0.18);
		float warn = 0.55 + 0.45 * pow(abs(sin(TIME * 1.6)), 3.0);
		float waves = 0.5 + 0.5 * sin(r * 12.0 - TIME * 1.4);
		e = (0.16 + 0.10 * waves + 0.75 * stripes * warn + 0.45 * rimGlow) ;
		col = mix(col, vec3(1.0, 0.18, 0.06), deny * stripes * 0.7);
	} else {
		float grid = max(band(p.x * 4.0 + 0.5, 0.035), band(p.y * 4.0 + 0.5, 0.035));
		float cross1 = 1.0 - smoothstep(0.02, 0.05, abs(p.x - p.y) * 0.7071);
		float cross2 = 1.0 - smoothstep(0.02, 0.05, abs(p.x + p.y) * 0.7071);
		e = 0.03 + 0.35 * grid + 0.6 * max(cross1, cross2) + 0.35 * rimGlow;
	}
	vec3 c = col * e * intensity * edge;
	ALBEDO = c;
	ALPHA = clamp(e * edge, 0.0, 1.0);
}
