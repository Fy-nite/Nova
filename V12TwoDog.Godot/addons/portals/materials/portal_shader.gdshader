shader_type spatial;
render_mode unshaded;

uniform sampler2D albedo: hint_default_black, source_color;

void fragment() {
#if 0
    // Desktop: screen-space mapping (correct for non-stereo rendering)
    vec3 portal_color = texture(albedo, SCREEN_UV).rgb;
#else
    // VR / fallback: use mesh UVs so both eyes sample the same texture position.
    // The SubViewport renders a single monoscopic view; using SCREEN_UV in stereo
    // gives each eye a different slice of the texture, causing a swapped-eyes look.
    vec3 portal_color = texture(albedo, UV).rgb;
#endif

	ALBEDO = portal_color;
}