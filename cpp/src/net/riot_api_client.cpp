#include "net/riot_api_client.h"
#include <iostream>

RiotApiClient::RiotApiClient() {
    curl_global_init(CURL_GLOBAL_DEFAULT);
    curl_ = curl_easy_init();
    if (curl_) {
        curl_easy_setopt(curl_, CURLOPT_SSL_VERIFYPEER, 0L);
        curl_easy_setopt(curl_, CURLOPT_SSL_VERIFYHOST, 0L);
        curl_easy_setopt(curl_, CURLOPT_TIMEOUT, 5L);
        curl_easy_setopt(curl_, CURLOPT_WRITEFUNCTION, write_callback);
        curl_easy_setopt(curl_, CURLOPT_WRITEDATA, &response_buffer_);
    }
}

RiotApiClient::~RiotApiClient() {
    if (curl_) curl_easy_cleanup(curl_);
    curl_global_cleanup();
}

size_t RiotApiClient::write_callback(char* data, size_t size,
                                      size_t nmemb, void* userdata) {
    auto* buf = static_cast<std::string*>(userdata);
    buf->append(data, size * nmemb);
    return size * nmemb;
}

std::optional<nlohmann::json> RiotApiClient::get_json(const std::string& url) {
    if (!curl_) return std::nullopt;

    response_buffer_.clear();
    curl_easy_setopt(curl_, CURLOPT_URL, url.c_str());
    CURLcode res = curl_easy_perform(curl_);

    if (res != CURLE_OK) return std::nullopt;

    long http_code = 0;
    curl_easy_getinfo(curl_, CURLINFO_RESPONSE_CODE, &http_code);
    if (http_code != 200) return std::nullopt;

    try {
        return nlohmann::json::parse(response_buffer_);
    } catch (...) {
        return std::nullopt;
    }
}

std::optional<nlohmann::json> RiotApiClient::get_active_player() {
    return get_json("https://127.0.0.1:2999/liveclientdata/activeplayer");
}

std::optional<nlohmann::json> RiotApiClient::get_champion_data(
    const std::string& champion_name) {
    std::string url =
        "https://raw.communitydragon.org/latest/game/data/characters/"
        + champion_name + "/" + champion_name + ".bin.json";
    return get_json(url);
}
