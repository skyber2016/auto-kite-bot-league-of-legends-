#pragma once
#include <string>
#include <optional>
#include <nlohmann/json.hpp>
#include <curl/curl.h>

class RiotApiClient {
public:
    RiotApiClient();
    ~RiotApiClient();

    RiotApiClient(const RiotApiClient&) = delete;
    RiotApiClient& operator=(const RiotApiClient&) = delete;

    std::optional<nlohmann::json> get_active_player();
    std::optional<nlohmann::json> get_champion_data(
        const std::string& champion_name);

private:
    std::optional<nlohmann::json> get_json(const std::string& url);
    static size_t write_callback(char* data, size_t size,
                                 size_t nmemb, void* userdata);

    CURL* curl_{};
    std::string response_buffer_;
};
