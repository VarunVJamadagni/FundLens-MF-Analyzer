(function () {
    "use strict";

    // This file is loaded on every page. Home-page logic only runs when the
    // search input exists (i.e. we are on Index.cshtml).
    var searchInput = document.getElementById("fl-search-input");
    if (!searchInput) {
        return;
    }

    var searchClearBtn = document.getElementById("fl-search-clear");
    var searchStatus = document.getElementById("fl-search-status");
    var resultsTitle = document.getElementById("fl-results-title");
    var resultsCount = document.getElementById("fl-results-count");
    var resultsGrid = document.getElementById("fl-results-grid");
    var resultsEmpty = document.getElementById("fl-results-empty");
    var resultsLoading = document.getElementById("fl-results-loading");
    var cardTemplate = document.getElementById("fl-fund-card-template");
    var filtersClearBtn = document.getElementById("fl-filters-clear");

    var filterGroups = {
        plan: document.getElementById("fl-filter-plan"),
        option: document.getElementById("fl-filter-option"),
        amc: document.getElementById("fl-filter-amc"),
        category: document.getElementById("fl-filter-category"),
        subcategory: document.getElementById("fl-filter-subcategory")
    };

    // Current in-memory data (whatever the API last returned - either the
    // eligible list or a search result set). Filters operate on this array
    // client-side; they never trigger new API calls.
    var currentFunds = [];
    var currentMode = "eligible"; // "eligible" | "search"
    var eligibleLoaded = false;
    var debounceTimer = null;
    var activeRequestToken = 0;

    function setLoading(isLoading) {
        resultsLoading.hidden = !isLoading;
        if (isLoading) {
            resultsGrid.innerHTML = "";
            resultsEmpty.hidden = true;
        }
    }

    function setStatus(text) {
        searchStatus.textContent = text || "";
    }

    async function fetchJson(url) {
        var response = await fetch(url, { headers: { "Accept": "application/json" } });
        if (!response.ok) {
            throw new Error("Request failed with status " + response.status);
        }
        return response.json();
    }

    async function loadEligible() {
        currentMode = "eligible";
        resultsTitle.textContent = "Eligible Funds";
        searchClearBtn.hidden = true;

        var token = ++activeRequestToken;
        setLoading(true);
        setStatus("Loading eligible funds\u2026");

        try {
            var data = await fetchJson("?handler=Eligible");
            if (token !== activeRequestToken) return;
            currentFunds = Array.isArray(data) ? data : [];
            eligibleLoaded = true;
            rebuildDynamicFilters(currentFunds);
            setStatus("");
        } catch (err) {
            if (token !== activeRequestToken) return;
            currentFunds = [];
            setStatus("Couldn't load eligible funds right now. Please try again.");
        } finally {
            if (token === activeRequestToken) {
                setLoading(false);
                renderResults();
            }
        }
    }

    async function runSearch(query) {
        currentMode = "search";
        resultsTitle.textContent = "Search Results";
        searchClearBtn.hidden = false;

        var token = ++activeRequestToken;
        setLoading(true);
        setStatus("Searching\u2026");

        try {
            var data = await fetchJson("?handler=Search&query=" + encodeURIComponent(query));
            if (token !== activeRequestToken) return;
            currentFunds = Array.isArray(data) ? data : [];
            rebuildDynamicFilters(currentFunds);
            setStatus(currentFunds.length === 0 ? "No funds matched '" + query + "'." : "");
        } catch (err) {
            if (token !== activeRequestToken) return;
            currentFunds = [];
            setStatus("Search failed. Please try again.");
        } finally {
            if (token === activeRequestToken) {
                setLoading(false);
                renderResults();
            }
        }
    }

    function getCheckedValues(container) {
        if (!container) return [];
        var boxes = container.querySelectorAll("input[type=checkbox]:checked");
        return Array.prototype.map.call(boxes, function (b) { return b.value; });
    }

    function fundMatchesFilters(fund) {
        var planChecked = getCheckedValues(filterGroups.plan);
        var optionChecked = getCheckedValues(filterGroups.option);
        var amcChecked = getCheckedValues(filterGroups.amc);
        var categoryChecked = getCheckedValues(filterGroups.category);
        var subCategoryChecked = getCheckedValues(filterGroups.subcategory);

        if (planChecked.length && planChecked.indexOf(fund.plan) === -1) return false;
        if (optionChecked.length && optionChecked.indexOf(fund.option) === -1) return false;
        if (amcChecked.length && amcChecked.indexOf(fund.fundHouse) === -1) return false;
        if (categoryChecked.length && categoryChecked.indexOf(fund.schemeCategory) === -1) return false;
        if (subCategoryChecked.length && subCategoryChecked.indexOf(fund.schemeSubCategory) === -1) return false;

        return true;
    }

    function metricClass(value) {
        if (!value || value === "N/A") return "fl-na";
        if (value.indexOf("-") === 0) return "fl-negative";
        return "fl-positive";
    }

    function renderResults() {
        var filtered = currentFunds.filter(fundMatchesFilters);

        resultsGrid.innerHTML = "";

        if (filtered.length === 0) {
            resultsEmpty.hidden = false;
            resultsCount.textContent = "";
            return;
        }

        resultsEmpty.hidden = true;
        resultsCount.textContent = filtered.length + (filtered.length === 1 ? " fund" : " funds");

        var fragment = document.createDocumentFragment();

        filtered.forEach(function (fund) {
            var node = cardTemplate.content.cloneNode(true);
            var anchor = node.querySelector(".fl-fund-card");
            anchor.href = "/Analytics?schemeCode=" + encodeURIComponent(fund.schemeCode);

            node.querySelector(".fl-fund-name").textContent = fund.schemeName || "Unknown Fund";
            node.querySelector(".fl-fund-plan").textContent = fund.plan || "Standard";
            node.querySelector(".fl-fund-nav-value").textContent = "\u20B9" + Number(fund.currentNAV || 0).toFixed(4);
            node.querySelector(".fl-fund-amc").textContent = fund.fundHouse || "";

            setMetric(node, ".fl-m1", fund.return1Month);
            setMetric(node, ".fl-m3", fund.return3Month);
            setMetric(node, ".fl-m6", fund.return6Month);
            setMetric(node, ".fl-y1", fund.cagr1Year);
            setMetric(node, ".fl-y3", fund.cagr3Year);
            setMetric(node, ".fl-y5", fund.cagr5Year);
            setMetric(node, ".fl-y10", fund.cagr10Year);

            fragment.appendChild(node);
        });

        resultsGrid.appendChild(fragment);
    }

    function setMetric(node, selector, value) {
        var el = node.querySelector(selector);
        el.textContent = value || "N/A";
        el.classList.add(metricClass(value));
    }

    function rebuildDynamicFilters(funds) {
        buildCheckboxOptions(filterGroups.amc, distinctValues(funds, "fundHouse"), "No AMC data available.");
        buildCheckboxOptions(filterGroups.category, distinctValues(funds, "schemeCategory"), "No category data available.");
        buildCheckboxOptions(filterGroups.subcategory, distinctValues(funds, "schemeSubCategory"), "No sub-category data available.");
    }

    function distinctValues(funds, propName) {
        var seen = {};
        var values = [];
        funds.forEach(function (fund) {
            var value = fund[propName];
            if (value && !seen[value]) {
                seen[value] = true;
                values.push(value);
            }
        });
        values.sort();
        return values;
    }

    function buildCheckboxOptions(container, values, emptyMessage) {
        if (!container) return;

        // Preserve currently checked values so re-rendering (e.g. after a
        // new search) doesn't silently drop a filter the user still wants,
        // where possible.
        var previouslyChecked = getCheckedValues(container);

        container.innerHTML = "";

        if (values.length === 0) {
            var p = document.createElement("p");
            p.className = "fl-filter-empty";
            p.textContent = emptyMessage;
            container.appendChild(p);
            return;
        }

        values.forEach(function (value) {
            var label = document.createElement("label");
            var input = document.createElement("input");
            input.type = "checkbox";
            input.value = value;
            if (previouslyChecked.indexOf(value) !== -1) {
                input.checked = true;
            }
            input.addEventListener("change", renderResults);

            label.appendChild(input);
            label.appendChild(document.createTextNode(" " + value));
            container.appendChild(label);
        });
    }

    function wireFilterSearchBoxes() {
        var searchBoxes = document.querySelectorAll(".fl-filter-search");
        searchBoxes.forEach(function (box) {
            box.addEventListener("input", function () {
                var targetContainer = document.getElementById(box.getAttribute("data-target"));
                if (!targetContainer) return;
                var term = box.value.trim().toLowerCase();
                var labels = targetContainer.querySelectorAll("label");
                labels.forEach(function (label) {
                    var text = label.textContent.trim().toLowerCase();
                    label.style.display = term === "" || text.indexOf(term) !== -1 ? "" : "none";
                });
            });
        });
    }

    function wireStaticFilterGroups() {
        [filterGroups.plan, filterGroups.option].forEach(function (group) {
            if (!group) return;
            group.querySelectorAll("input[type=checkbox]").forEach(function (box) {
                box.addEventListener("change", renderResults);
            });
        });
    }

    function clearAllFilters() {
        document.querySelectorAll("#fl-filters input[type=checkbox]").forEach(function (box) {
            box.checked = false;
        });
        document.querySelectorAll(".fl-filter-search").forEach(function (box) {
            box.value = "";
        });
        document.querySelectorAll("#fl-filters label").forEach(function (label) {
            label.style.display = "";
        });
        renderResults();
    }

    searchInput.addEventListener("focus", function () {
        if (searchInput.value.trim() === "" && !eligibleLoaded) {
            loadEligible();
        }
    });

    searchInput.addEventListener("input", function () {
        var value = searchInput.value.trim();
        searchClearBtn.hidden = value === "";

        if (debounceTimer) {
            clearTimeout(debounceTimer);
        }

        debounceTimer = setTimeout(function () {
            if (value === "") {
                loadEligible();
            } else {
                runSearch(value);
            }
        }, 300);
    });

    searchClearBtn.addEventListener("click", function () {
        searchInput.value = "";
        searchClearBtn.hidden = true;
        searchInput.focus();
        loadEligible();
    });

    if (filtersClearBtn) {
        filtersClearBtn.addEventListener("click", clearAllFilters);
    }

    wireFilterSearchBoxes();
    wireStaticFilterGroups();
})();
