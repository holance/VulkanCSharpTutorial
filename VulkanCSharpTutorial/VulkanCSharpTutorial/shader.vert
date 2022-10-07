#version 450

layout(set = 0, binding = 0) uniform GlobalVar {
    mat4 view;
    mat4 proj;
    mat4 viewProj;
};

layout(push_constant) uniform ModelVar {
    mat4 world;
};

layout(location = 0) in vec3 position;
layout(location = 1) in vec4 color;

layout(location = 0) out vec4 fragColor;

void main() {
    gl_Position = viewProj * world * vec4(position, 1);
    fragColor = color;
}