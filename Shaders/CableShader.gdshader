shader_type spatial;
render_mode unshaded;

uniform vec3 color : source_color = vec3(0.0, 0.8, 1.0);
uniform float speed : hint_range(0.1, 10.0) = 3.0;
uniform float pulse_width : hint_range(0.1, 1.0) = 0.4;
uniform float frequency : hint_range(0.1, 10.0) = 2.0;
uniform float cable_length; // MUST be set from C#

varying float dist_from_start;

void vertex() {
    // VERTEX.z ranges from -(length/2) to +(length/2).
    // By adding (length/2), it now ranges from 0 to length.
    dist_from_start = VERTEX.z + (cable_length / 2.0);
}

void fragment() {
    // Now dist_from_start is a linear ruler from one end to the other.
    // Use '+' or '-' to control if it flows to parent or child.
    float pulse = sin((dist_from_start * frequency) - (TIME * speed));

    pulse = smoothstep(1.0 - pulse_width, 1.0, pulse);

    ALBEDO = color * pulse;
    ALBEDO += color * 0.15;
    ALPHA = clamp(pulse + 0.3, 0.0, 1.0);
}