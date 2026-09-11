(function () {
    "use strict";

    var searchInput = document.getElementById("fl-search-input");

    // This file is loaded on every page.
    // Only run the fund-list logic when the search input exists.
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

    // All funds returned by the current search.
    var currentFunds = [];

    // Current page for client-side pagination.
    var currentPage = 1;

    // Number of funds displayed per page.
    var pageSize = 15;

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
        var response = await fetch(url, {
            headers: {
                "Accept": "application/json"
            }
        });

        if (!response.ok) {
            throw new Error("Request failed with status " + response.status);
        }

        return response.json();
    }

    /*
     * Search the API.
     *
     * IMPORTANT:
     * There is deliberately NO eligible-funds request here.
     *
     * The page remains empty until the user searches.
     */
    async function runSearch(query) {
        var token = ++activeRequestToken;

        currentPage = 1;

        resultsTitle.textContent = "Search Results";
        searchClearBtn.hidden = false;

        setLoading(true);
        setStatus("Searching...");

        try {
            var data = await fetchJson(
                "?handler=Search&query=" + encodeURIComponent(query)
            );

            if (token !== activeRequestToken) {
                return;
            }

            currentFunds = Array.isArray(data) ? data : [];

            rebuildDynamicFilters(currentFunds);

            if (currentFunds.length === 0) {
                setStatus("No funds matched your search.");
            } else {
                setStatus("");
            }

            renderResults();
        }
        catch (err) {
            if (token !== activeRequestToken) {
                return;
            }

            currentFunds = [];

            rebuildDynamicFilters(currentFunds);

            resultsGrid.innerHTML = "";
            resultsEmpty.hidden = false;
            resultsCount.textContent = "";

            setStatus("Search failed. Please try again.");
        }
        finally {
            if (token === activeRequestToken) {
                setLoading(false);
            }
        }
    }

    /*
     * Returns all checked values from a filter group.
     */
    function getCheckedValues(container) {
        if (!container) {
            return [];
        }

        var boxes = container.querySelectorAll(
            "input[type=checkbox]:checked"
        );

        return Array.prototype.map.call(
            boxes,
            function (box) {
                return box.value;
            }
        );
    }

    /*
     * Determines whether a fund matches all currently selected filters.
     */
    function fundMatchesFilters(fund) {
        var planChecked = getCheckedValues(filterGroups.plan);
        var optionChecked = getCheckedValues(filterGroups.option);
        var amcChecked = getCheckedValues(filterGroups.amc);
        var categoryChecked = getCheckedValues(filterGroups.category);
        var subCategoryChecked = getCheckedValues(filterGroups.subcategory);

        if (
            planChecked.length &&
            planChecked.indexOf(fund.plan) === -1
        ) {
            return false;
        }

        if (
            optionChecked.length &&
            optionChecked.indexOf(fund.option) === -1
        ) {
            return false;
        }

        if (
            amcChecked.length &&
            amcChecked.indexOf(fund.fundHouse) === -1
        ) {
            return false;
        }

        if (
            categoryChecked.length &&
            categoryChecked.indexOf(fund.schemeCategory) === -1
        ) {
            return false;
        }

        if (
            subCategoryChecked.length &&
            subCategoryChecked.indexOf(fund.schemeSubCategory) === -1
        ) {
            return false;
        }

        return true;
    }

    function metricClass(value) {
        if (!value || value === "N/A") {
            return "fl-na";
        }

        if (value.indexOf("-") === 0) {
            return "fl-negative";
        }

        return "fl-positive";
    }

    /*
     * Render the currently filtered page.
     */
    function renderResults() {
        var filtered = currentFunds.filter(fundMatchesFilters);

        var totalCount = filtered.length;
        var totalPages = Math.max(
            1,
            Math.ceil(totalCount / pageSize)
        );

        // Make sure current page is still valid after filtering.
        if (currentPage > totalPages) {
            currentPage = totalPages;
        }

        resultsGrid.innerHTML = "";

        if (totalCount === 0) {
            resultsEmpty.hidden = false;
            resultsCount.textContent = "";
            removePagination();
            return;
        }

        resultsEmpty.hidden = true;

        resultsCount.textContent =
            totalCount +
            (totalCount === 1 ? " fund" : " funds");

        var startIndex = (currentPage - 1) * pageSize;
        var endIndex = Math.min(
            startIndex + pageSize,
            totalCount
        );

        var pageFunds = filtered.slice(
            startIndex,
            endIndex
        );

        var fragment = document.createDocumentFragment();

        pageFunds.forEach(function (fund) {
            var node = cardTemplate.content.cloneNode(true);

            var anchor = node.querySelector(".fl-fund-card");

            anchor.href =
                "/Analytics?schemeCode=" +
                encodeURIComponent(fund.schemeCode);

            node.querySelector(".fl-fund-name").textContent =
                fund.schemeName || "Unknown Fund";

            node.querySelector(".fl-fund-plan").textContent =
                fund.plan || "Standard";

            node.querySelector(".fl-fund-nav-value").textContent =
                "₹" +
                Number(fund.currentNAV || 0).toFixed(4);

            node.querySelector(".fl-fund-amc").textContent =
                fund.fundHouse || "";

            setMetric(
                node,
                ".fl-m1",
                fund.return1Month
            );

            setMetric(
                node,
                ".fl-m3",
                fund.return3Month
            );

            setMetric(
                node,
                ".fl-m6",
                fund.return6Month
            );

            setMetric(
                node,
                ".fl-y1",
                fund.cagr1Year
            );

            setMetric(
                node,
                ".fl-y3",
                fund.cagr3Year
            );

            setMetric(
                node,
                ".fl-y5",
                fund.cagr5Year
            );

            setMetric(
                node,
                ".fl-y10",
                fund.cagr10Year
            );

            fragment.appendChild(node);
        });

        resultsGrid.appendChild(fragment);

        renderPagination(totalPages);
    }

    function setMetric(node, selector, value) {
        var element = node.querySelector(selector);

        element.textContent = value || "N/A";
        element.classList.add(metricClass(value));
    }

    /*
     * Pagination controls.
     */
    function renderPagination(totalPages) {
        removePagination();

        if (totalPages <= 1) {
            return;
        }

        var pagination = document.createElement("div");
        pagination.id = "fl-pagination";
        pagination.className = "fl-pagination";

        var previousButton = document.createElement("button");

        previousButton.type = "button";
        previousButton.className = "fl-pagination-button";
        previousButton.textContent = "Previous";
        previousButton.disabled = currentPage === 1;

        previousButton.addEventListener("click", function () {
            if (currentPage > 1) {
                currentPage--;
                renderResults();

                scrollToResults();
            }
        });

        pagination.appendChild(previousButton);

        /*
         * Keep the pagination compact when there are many pages.
         */
        var pages = getPageNumbers(totalPages);

        pages.forEach(function (page) {
            if (page === "...") {
                var ellipsis = document.createElement("span");

                ellipsis.className = "fl-pagination-ellipsis";
                ellipsis.textContent = "...";

                pagination.appendChild(ellipsis);
                return;
            }

            var pageButton = document.createElement("button");

            pageButton.type = "button";
            pageButton.className =
                "fl-pagination-button" +
                (page === currentPage
                    ? " active"
                    : "");

            pageButton.textContent = page;

            pageButton.addEventListener("click", function () {
                currentPage = page;

                renderResults();

                scrollToResults();
            });

            pagination.appendChild(pageButton);
        });

        var nextButton = document.createElement("button");

        nextButton.type = "button";
        nextButton.className = "fl-pagination-button";
        nextButton.textContent = "Next";
        nextButton.disabled = currentPage === totalPages;

        nextButton.addEventListener("click", function () {
            if (currentPage < totalPages) {
                currentPage++;
                renderResults();

                scrollToResults();
            }
        });

        pagination.appendChild(nextButton);

        var resultsSection = document.querySelector(".fl-results");

        if (resultsSection) {
            resultsSection.appendChild(pagination);
        }
    }

    function getPageNumbers(totalPages) {
        var pages = [];

        if (totalPages <= 7) {
            for (var i = 1; i <= totalPages; i++) {
                pages.push(i);
            }

            return pages;
        }

        pages.push(1);

        if (currentPage > 4) {
            pages.push("...");
        }

        var start = Math.max(2, currentPage - 1);
        var end = Math.min(
            totalPages - 1,
            currentPage + 1
        );

        for (var page = start; page <= end; page++) {
            pages.push(page);
        }

        if (currentPage < totalPages - 3) {
            pages.push("...");
        }

        pages.push(totalPages);

        return pages;
    }

    function removePagination() {
        var existing = document.getElementById("fl-pagination");

        if (existing) {
            existing.remove();
        }
    }

    function scrollToResults() {
        var resultsSection = document.querySelector(".fl-results");

        if (resultsSection) {
            resultsSection.scrollIntoView({
                behavior: "smooth",
                block: "start"
            });
        }
    }

    /*
     * Rebuild AMC, Category and Sub-Category filters
     * from the current search result set.
     */
    function rebuildDynamicFilters(funds) {
        buildCheckboxOptions(
            filterGroups.amc,
            distinctValues(funds, "fundHouse"),
            "No AMC data available."
        );

        buildCheckboxOptions(
            filterGroups.category,
            distinctValues(funds, "schemeCategory"),
            "No category data available."
        );

        buildCheckboxOptions(
            filterGroups.subcategory,
            distinctValues(funds, "schemeSubCategory"),
            "No sub-category data available."
        );
    }

    function distinctValues(funds, propertyName) {
        var seen = {};
        var values = [];

        funds.forEach(function (fund) {
            var value = fund[propertyName];

            if (value && !seen[value]) {
                seen[value] = true;
                values.push(value);
            }
        });

        values.sort();

        return values;
    }

    function buildCheckboxOptions(
        container,
        values,
        emptyMessage
    ) {
        if (!container) {
            return;
        }

        var previouslyChecked =
            getCheckedValues(container);

        container.innerHTML = "";

        if (values.length === 0) {
            var empty = document.createElement("p");

            empty.className = "fl-filter-empty";
            empty.textContent = emptyMessage;

            container.appendChild(empty);

            return;
        }

        values.forEach(function (value) {
            var label = document.createElement("label");
            var input = document.createElement("input");

            input.type = "checkbox";
            input.value = value;

            if (
                previouslyChecked.indexOf(value) !== -1
            ) {
                input.checked = true;
            }

            input.addEventListener(
                "change",
                function () {
                    currentPage = 1;
                    renderResults();
                }
            );

            label.appendChild(input);

            label.appendChild(
                document.createTextNode(
                    " " + value
                )
            );

            container.appendChild(label);
        });
    }

    /*
     * Search boxes inside AMC/category/sub-category filters.
     */
    function wireFilterSearchBoxes() {
        var searchBoxes =
            document.querySelectorAll(
                ".fl-filter-search"
            );

        searchBoxes.forEach(function (box) {
            box.addEventListener(
                "input",
                function () {
                    var targetContainer =
                        document.getElementById(
                            box.getAttribute(
                                "data-target"
                            )
                        );

                    if (!targetContainer) {
                        return;
                    }

                    var term =
                        box.value
                            .trim()
                            .toLowerCase();

                    var labels =
                        targetContainer.querySelectorAll(
                            "label"
                        );

                    labels.forEach(
                        function (label) {
                            var text =
                                label.textContent
                                    .trim()
                                    .toLowerCase();

                            label.style.display =
                                term === "" ||
                                text.indexOf(term) !== -1
                                    ? ""
                                    : "none";
                        }
                    );
                }
            );
        });
    }

    function wireStaticFilterGroups() {
        [
            filterGroups.plan,
            filterGroups.option
        ].forEach(function (group) {
            if (!group) {
                return;
            }

            group
                .querySelectorAll(
                    "input[type=checkbox]"
                )
                .forEach(function (box) {
                    box.addEventListener(
                        "change",
                        function () {
                            currentPage = 1;
                            renderResults();
                        }
                    );
                });
        });
    }

    /*
     * Clears all filters and returns to page 1
     * of the current search.
     */
    function clearAllFilters() {
        document
            .querySelectorAll(
                "#fl-filters input[type=checkbox]"
            )
            .forEach(function (box) {
                box.checked = false;
            });

        document
            .querySelectorAll(
                ".fl-filter-search"
            )
            .forEach(function (box) {
                box.value = "";
            });

        document
            .querySelectorAll(
                "#fl-filters label"
            )
            .forEach(function (label) {
                label.style.display = "";
            });

        currentPage = 1;

        renderResults();
    }

    /*
     * Search input.
     *
     * Empty search = clear the page.
     * It does NOT load Eligible Funds.
     */
    searchInput.addEventListener(
        "input",
        function () {
            var value =
                searchInput.value.trim();

            searchClearBtn.hidden =
                value === "";

            if (debounceTimer) {
                clearTimeout(debounceTimer);
            }

            debounceTimer = setTimeout(
                function () {
                    if (value === "") {
                        currentFunds = [];
                        currentPage = 1;

                        resultsTitle.textContent =
                            "Search Funds";

                        resultsCount.textContent = "";

                        resultsGrid.innerHTML = "";

                        resultsEmpty.hidden = true;

                        removePagination();

                        rebuildDynamicFilters(
                            currentFunds
                        );

                        setStatus("");

                        return;
                    }

                    runSearch(value);
                },
                300
            );
        }
    );

    /*
     * Clear search.
     */
    searchClearBtn.addEventListener(
        "click",
        function () {
            searchInput.value = "";

            searchClearBtn.hidden = true;

            currentFunds = [];
            currentPage = 1;

            resultsTitle.textContent =
                "Search Funds";

            resultsCount.textContent = "";

            resultsGrid.innerHTML = "";

            resultsEmpty.hidden = true;

            removePagination();

            rebuildDynamicFilters(
                currentFunds
            );

            setStatus("");

            searchInput.focus();
        }
    );

    if (filtersClearBtn) {
        filtersClearBtn.addEventListener(
            "click",
            clearAllFilters
        );
    }

    wireFilterSearchBoxes();
    wireStaticFilterGroups();

    /*
     * IMPORTANT:
     * Nothing is loaded here.
     *
     * The page starts empty and only searches
     * after the user enters something.
     */
})();