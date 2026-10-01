#include "core/settings.h"
#include <fstream>
#include <iostream>

Settings Settings::load(const std::filesystem::path& path) {
    std::ifstream file(path);
    if (!file.is_open()) {
        std::cerr << "Settings file not found, using defaults.\n";
        return Settings{};
    }
    try {
        nlohmann::json j;
        file >> j;
        return j.get<Settings>();
    } catch (const std::exception& e) {
        std::cerr << "Failed to parse settings JSON: " << e.what() << ", using defaults.\n";
        return Settings{};
    }
}

void Settings::create_new(const std::filesystem::path& path) {
    Settings defaults;
    defaults.save(path);
}

void Settings::save(const std::filesystem::path& path) const {
    namespace fs = std::filesystem;
    if (path.has_parent_path())
        fs::create_directories(path.parent_path());
    std::ofstream file(path);
    if (!file.is_open()) {
        std::cerr << "[Settings] Failed to open file for writing: " << path << std::endl;
        return;
    }
    nlohmann::json j = *this;
    file << j.dump(4);
}
